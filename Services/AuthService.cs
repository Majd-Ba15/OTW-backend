using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Text;
using MailKit.Net.Smtp;
using MimeKit;
using OTW.Api.Data;
using OTW.Api.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace OTW.Api.Services;

public class AuthService : IAuthService {
    readonly AppDbContext  _db;
    readonly IEmailService _email;
    readonly AppSettings   _cfg;
    public AuthService(AppDbContext db, IEmailService email, IOptions<AppSettings> cfg) { _db=db; _email=email; _cfg=cfg.Value; }

    // ── OTP brute-force guard (in-memory, keyed by userId) ─────────
    // Caps wrong attempts inside a rolling window so a 6-digit code
    // (1,000,000 combinations) cannot be brute-forced during its validity.
    // NOTE: process-local — for multi-instance hosting move this to a shared
    // store (DB / distributed cache) instead of a static dictionary.
    static readonly ConcurrentDictionary<int,(int Count,DateTime WindowStart)> _otpAttempts = new();
    const int MaxOtpAttempts = 5;
    static readonly TimeSpan OtpAttemptWindow = TimeSpan.FromMinutes(10);

    // Cryptographically-secure 6-digit OTP (replaces predictable System.Random).
    static string GenerateOtp() => RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();

    public async Task<ServiceResult> RegisterAsync(RegisterRequest req) {
        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return new() { Success=false, Message="Email already registered" };
        var user = new User { FullName=req.FullName, Email=req.Email, PasswordHash=BCrypt.Net.BCrypt.HashPassword(req.Password), Role=req.Role, University=req.University };
        if (_cfg.RequireOTP) {
            var otp = GenerateOtp();
            user.OtpCode=otp; user.OtpExpiry=DateTime.UtcNow.AddMinutes(10);
            _db.Users.Add(user); await _db.SaveChangesAsync();
            await _email.SendOtpAsync(user.Email, user.FullName, otp);
            return new() { Success=true, Message="OTP sent to email", UserId=user.UserId };
        }
        user.IsEmailVerified=true; _db.Users.Add(user); await _db.SaveChangesAsync();
        return new() { Success=true, Message="Registered successfully", UserId=user.UserId };
    }
    public async Task<ServiceResult> VerifyOtpAsync(int userId, string otp) {
        var u = await _db.Users.FindAsync(userId);
        if (u==null) return new() { Success=false, Message="User not found" };

        // Brute-force guard: reset a stale window, then block once the cap is hit.
        var now = DateTime.UtcNow;
        var attempt = _otpAttempts.GetValueOrDefault(userId);
        if (attempt.Count > 0 && now - attempt.WindowStart > OtpAttemptWindow) {
            _otpAttempts.TryRemove(userId, out _);
            attempt = default;
        }
        if (attempt.Count >= MaxOtpAttempts)
            return new() { Success=false, Message="Too many incorrect attempts. Please request a new code." };

        if (u.OtpCode!=otp) {
            _otpAttempts[userId] = (attempt.Count + 1, attempt.Count == 0 ? now : attempt.WindowStart);
            return new() { Success=false, Message="Invalid OTP" };
        }
        if (u.OtpExpiry<now) return new() { Success=false, Message="OTP expired" };
        _otpAttempts.TryRemove(userId, out _);   // success → clear the counter
        u.IsEmailVerified=true; u.OtpCode=null; u.OtpExpiry=null;
        await _db.SaveChangesAsync();
        // Issue the JWT here (like LoginAsync) so the user is authenticated for
        // the onboarding steps that follow — profile photo and student-ID upload
        // are [Authorize] endpoints and 401 without it.
        return new() { Success=true, Token=GenerateJwt(u), UserId=u.UserId, Role=u.Role, FullName=u.FullName };
    }
    public async Task<ServiceResult> ResendOtpAsync(int userId) {
        var u = await _db.Users.FindAsync(userId);
        if (u==null) return new() { Success=false, Message="Not found" };
        var otp = GenerateOtp();
        u.OtpCode=otp; u.OtpExpiry=DateTime.UtcNow.AddMinutes(10);
        _otpAttempts.TryRemove(userId, out _);   // fresh code → fresh attempt allowance
        await _db.SaveChangesAsync();
        await _email.SendOtpAsync(u.Email, u.FullName, otp);
        return new() { Success=true };
    }
    public async Task<ServiceResult> LoginAsync(string email, string password) {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Email==email);
        if (u==null||!BCrypt.Net.BCrypt.Verify(password,u.PasswordHash))
            return new() { Success=false, Message="Invalid credentials" };
        if (!u.IsActive) return new() { Success=false, Message="Account suspended" };
        return new() { Success=true, Token=GenerateJwt(u), UserId=u.UserId, Role=u.Role, FullName=u.FullName };
    }
    public async Task<ServiceResult> ForgotPasswordAsync(string email) {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Email==email);
        if (u!=null) {
            var t=Guid.NewGuid().ToString("N");
            u.OtpCode=t; u.OtpExpiry=DateTime.UtcNow.AddMinutes(15);
            await _db.SaveChangesAsync();
            await _email.SendPasswordResetAsync(u.Email,u.FullName,t);
        }
        return new() { Success=true };
    }
    public async Task<ServiceResult> ResetPasswordAsync(string token, string newPass) {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.OtpCode==token && x.OtpExpiry>DateTime.UtcNow);
        if (u==null) return new() { Success=false, Message="Invalid or expired token" };
        u.PasswordHash=BCrypt.Net.BCrypt.HashPassword(newPass); u.OtpCode=null; u.OtpExpiry=null;
        await _db.SaveChangesAsync();
        return new() { Success=true };
    }
    string GenerateJwt(User u) {
        var key   = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_cfg.JwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: [new("userId",u.UserId.ToString()),new("role",u.Role),new(ClaimTypes.Role,u.Role)],
            expires: DateTime.UtcNow.AddDays(_cfg.JwtExpiryDays), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

