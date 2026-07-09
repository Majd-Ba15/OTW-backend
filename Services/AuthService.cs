using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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

    public async Task<ServiceResult> RegisterAsync(RegisterRequest req) {
        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return new() { Success=false, Message="Email already registered" };
        var user = new User { FullName=req.FullName, Email=req.Email, PasswordHash=BCrypt.Net.BCrypt.HashPassword(req.Password), Role=req.Role, University=req.University };
        if (_cfg.RequireOTP) {
            var otp = new Random().Next(100000,999999).ToString();
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
        if (u.OtpCode!=otp) return new() { Success=false, Message="Invalid OTP" };
        if (u.OtpExpiry<DateTime.UtcNow) return new() { Success=false, Message="OTP expired" };
        u.IsEmailVerified=true; u.OtpCode=null; u.OtpExpiry=null;
        await _db.SaveChangesAsync();
        // Issue the JWT here (like LoginAsync) so the user is authenticated for
        // the onboarding steps that follow â€” profile photo and student-ID upload
        // are [Authorize] endpoints and 401 without it.
        return new() { Success=true, Token=GenerateJwt(u), UserId=u.UserId, Role=u.Role, FullName=u.FullName };
    }
    public async Task<ServiceResult> ResendOtpAsync(int userId) {
        var u = await _db.Users.FindAsync(userId);
        if (u==null) return new() { Success=false, Message="Not found" };
        var otp = new Random().Next(100000,999999).ToString();
        u.OtpCode=otp; u.OtpExpiry=DateTime.UtcNow.AddMinutes(10);
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

