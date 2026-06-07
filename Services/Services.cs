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

// ── AUTH SERVICE ──────────────────────────────
public interface IAuthService {
    Task<ServiceResult> RegisterAsync(RegisterRequest req);
    Task<ServiceResult> VerifyOtpAsync(int userId, string otp);
    Task<ServiceResult> ResendOtpAsync(int userId);
    Task<ServiceResult> LoginAsync(string email, string password);
    Task<ServiceResult> ForgotPasswordAsync(string email);
    Task<ServiceResult> ResetPasswordAsync(string token, string newPass);
}
public class AuthService : IAuthService {
    readonly AppDbContext  _db;
    readonly IEmailService _email;
    readonly AppSettings   _cfg;
    public AuthService(AppDbContext db, IEmailService email, IOptions<AppSettings> cfg) { _db=db; _email=email; _cfg=cfg.Value; }

    public async Task<ServiceResult> RegisterAsync(RegisterRequest req) {
        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return new() { Success=false, Message="Email already registered" };
        var user = new User { FullName=req.FullName, Email=req.Email, PasswordHash=BCrypt.Net.BCrypt.HashPassword(req.Password), Role=req.Role };
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
        return new() { Success=true };
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

// ── EMAIL SERVICE ─────────────────────────────
public interface IEmailService {
    Task SendOtpAsync(string to, string name, string otp);
    Task SendVerificationApprovedAsync(string to, string name);
    Task SendVerificationRejectedAsync(string to, string name, string reason);
    Task SendPasswordResetAsync(string to, string name, string token);
    Task SendWarningAsync(string to, string name, string note);
    // ── New booking emails ─────────────────────
    Task SendBookingAcceptedAsync(string to, string riderName, string from, string to2, string date, string driverName, string carPlate);
    Task SendBookingDeclinedAsync(string to, string riderName, string from, string to2, string date);
    Task SendRideReminderAsync(string to, string riderName, string from, string to2, string date, string driverName, string carPlate, string carModel);
    Task SendRateRideAsync(string to, string userName, string role, string otherName, int bookingId);
    Task SendWaitlistPromotedAsync(string to, string riderName, string from, string to2, string date, int rideId);
    Task SendAdminVerificationReminderAsync(string adminEmail, string driverName, string driverEmail, int userId);
}

public class EmailService : IEmailService {
    readonly MailSettings _cfg;
    public EmailService(IOptions<MailSettings> cfg) => _cfg=cfg.Value;

    // ── shared styled HTML wrapper ─────────────
    static string Layout(string body) => $@"
        <div style='font-family:Arial,sans-serif;max-width:520px;margin:0 auto;background:#fff;border-radius:12px;overflow:hidden;border:1px solid #e5e5e5'>
          <div style='background:#16a36b;padding:20px 28px;text-align:center'>
            <span style='font-size:28px;font-weight:700;letter-spacing:6px;color:#fff'>OTW</span>
            <div style='font-size:12px;color:rgba(255,255,255,0.8);letter-spacing:3px;margin-top:2px'>ON THE WAY</div>
          </div>
          <div style='padding:24px 28px;color:#1a1a1a;line-height:1.6'>{body}</div>
          <div style='background:#f5f5f5;padding:12px 28px;text-align:center;font-size:11px;color:#888'>
            OTW — University Carpooling · Do not reply to this email
          </div>
        </div>";

    async Task Send(string to,string name,string subject,string html) {
        try {
            var msg=new MimeMessage();
            msg.From.Add(new MailboxAddress(_cfg.DisplayName,_cfg.Email));
            msg.To.Add(new MailboxAddress(name,to));
            msg.Subject=subject;
            msg.Body=new TextPart("html"){Text=html};
            using var c=new SmtpClient();
            await c.ConnectAsync("smtp.gmail.com",587,false);
            await c.AuthenticateAsync(_cfg.Email,_cfg.AppPassword);
            await c.SendAsync(msg);
            await c.DisconnectAsync(true);
        } catch(Exception ex) { Console.WriteLine($"Email error: {ex.Message}"); }
    }

    // ── Original 5 methods ─────────────────────
    public Task SendOtpAsync(string to,string name,string otp) =>
        Send(to,name,"OTW — Verify your email", Layout(
            $"<h2 style='color:#16a36b'>Hi {name}!</h2>" +
            $"<p>Your OTW verification code is:</p>" +
            $"<div style='text-align:center;margin:20px 0'><span style='font-size:36px;font-weight:700;letter-spacing:12px;color:#16a36b'>{otp}</span></div>" +
            $"<p style='color:#888;font-size:12px'>This code expires in 10 minutes. If you did not register on OTW, ignore this email.</p>"));

    public Task SendVerificationApprovedAsync(string to,string name) =>
        Send(to,name,"OTW — Account verified ✓", Layout(
            $"<h2 style='color:#16a36b'>Hi {name}! 🎉</h2>" +
            $"<p>Great news — your student ID has been reviewed and your account is now <strong>verified</strong>.</p>" +
            $"<p>You can now search and book rides on OTW. Welcome to the community!</p>" +
            $"<div style='text-align:center;margin:20px 0'><a href='http://localhost:3000/auth/login' style='background:#16a36b;color:#fff;padding:12px 28px;border-radius:8px;text-decoration:none;font-weight:600'>Open OTW</a></div>"));

    public Task SendVerificationRejectedAsync(string to,string name,string reason) =>
        Send(to,name,"OTW — Verification unsuccessful", Layout(
            $"<h2 style='color:#1a1a1a'>Hi {name},</h2>" +
            $"<p>Unfortunately your student ID could not be verified.</p>" +
            $"<div style='background:#FCEBEB;border-radius:8px;padding:12px 16px;margin:16px 0;color:#A32D2D'><strong>Reason:</strong> {reason}</div>" +
            $"<p>Please log in, go to your profile, and upload a clearer photo of your student ID. Make sure your name and student number are clearly visible.</p>" +
            $"<div style='text-align:center;margin:20px 0'><a href='http://localhost:3000/auth/login' style='background:#16a36b;color:#fff;padding:12px 28px;border-radius:8px;text-decoration:none;font-weight:600'>Upload again</a></div>"));

    public Task SendPasswordResetAsync(string to,string name,string token) =>
        Send(to,name,"OTW — Reset your password", Layout(
            $"<h2 style='color:#1a1a1a'>Hi {name},</h2>" +
            $"<p>We received a request to reset your password. Click the button below to set a new one.</p>" +
            $"<div style='text-align:center;margin:24px 0'><a href='http://localhost:3000/auth/reset-password?token={token}' style='background:#16a36b;color:#fff;padding:12px 28px;border-radius:8px;text-decoration:none;font-weight:600'>Reset password</a></div>" +
            $"<p style='color:#888;font-size:12px'>This link expires in 15 minutes. If you did not request a password reset, ignore this email.</p>"));

    public Task SendWarningAsync(string to,string name,string note) =>
        Send(to,name,"OTW — Account warning", Layout(
            $"<h2 style='color:#854F0B'>Hi {name},</h2>" +
            $"<p>You have received a warning from the OTW admin team.</p>" +
            $"<div style='background:#FAEEDA;border-radius:8px;padding:12px 16px;margin:16px 0;color:#854F0B'>{note}</div>" +
            $"<p>Please review our community guidelines. Repeated violations may result in account suspension.</p>"));

    // ── New 5 booking-related emails ──────────
    public Task SendBookingAcceptedAsync(string to,string riderName,string from,string to2,string date,string driverName,string carPlate) =>
        Send(to,riderName,"OTW — Your booking is confirmed ✓", Layout(
            $"<h2 style='color:#16a36b'>Great news, {riderName}! 🎉</h2>" +
            $"<p>Your booking has been <strong>accepted</strong> by the driver.</p>" +
            $"<div style='background:#E1F5EE;border-radius:8px;padding:16px;margin:16px 0'>" +
            $"  <table style='width:100%;font-size:13px;border-collapse:collapse'>" +
            $"    <tr><td style='color:#888;padding:4px 0'>Route</td><td style='font-weight:600'>{from} → {to2}</td></tr>" +
            $"    <tr><td style='color:#888;padding:4px 0'>Date & time</td><td style='font-weight:600'>{date}</td></tr>" +
            $"    <tr><td style='color:#888;padding:4px 0'>Driver</td><td style='font-weight:600'>{driverName}</td></tr>" +
            $"    <tr><td style='color:#888;padding:4px 0'>Car plate</td><td style='font-weight:600'>{carPlate}</td></tr>" +
            $"    <tr><td style='color:#888;padding:4px 0'>Payment</td><td style='font-weight:600'>Cash to driver</td></tr>" +
            $"  </table>" +
            $"</div>" +
            $"<p>Please be at the pickup point on time. You can message your driver directly in the OTW app.</p>" +
            $"<div style='text-align:center;margin:20px 0'><a href='http://localhost:3000/rider/dashboard' style='background:#16a36b;color:#fff;padding:12px 28px;border-radius:8px;text-decoration:none;font-weight:600'>View in OTW</a></div>"));

    public Task SendBookingDeclinedAsync(string to,string riderName,string from,string to2,string date) =>
        Send(to,riderName,"OTW — Booking not accepted", Layout(
            $"<h2 style='color:#1a1a1a'>Hi {riderName},</h2>" +
            $"<p>Unfortunately your booking request was <strong>not accepted</strong> by the driver.</p>" +
            $"<div style='background:#f5f5f5;border-radius:8px;padding:16px;margin:16px 0;font-size:13px'>" +
            $"  <div><span style='color:#888'>Route:</span> <strong>{from} → {to2}</strong></div>" +
            $"  <div style='margin-top:6px'><span style='color:#888'>Date:</span> <strong>{date}</strong></div>" +
            $"</div>" +
            $"<p>Don't worry — there are other rides available. Search for an alternative on OTW.</p>" +
            $"<div style='text-align:center;margin:20px 0'><a href='http://localhost:3000/rider/search' style='background:#16a36b;color:#fff;padding:12px 28px;border-radius:8px;text-decoration:none;font-weight:600'>Find another ride</a></div>"));

    public Task SendRideReminderAsync(string to,string riderName,string from,string to2,string date,string driverName,string carPlate,string carModel) =>
        Send(to,riderName,"OTW — Your ride is in 1 hour 🚗", Layout(
            $"<h2 style='color:#1a1a1a'>Hi {riderName}, your ride is soon!</h2>" +
            $"<p>Reminder — you have a confirmed ride departing in approximately <strong>1 hour</strong>.</p>" +
            $"<div style='background:#E6F1FB;border-radius:8px;padding:16px;margin:16px 0'>" +
            $"  <table style='width:100%;font-size:13px;border-collapse:collapse'>" +
            $"    <tr><td style='color:#888;padding:4px 0'>Pickup</td><td style='font-weight:600'>{from}</td></tr>" +
            $"    <tr><td style='color:#888;padding:4px 0'>Destination</td><td style='font-weight:600'>{to2}</td></tr>" +
            $"    <tr><td style='color:#888;padding:4px 0'>Departure</td><td style='font-weight:600'>{date}</td></tr>" +
            $"    <tr><td style='color:#888;padding:4px 0'>Driver</td><td style='font-weight:600'>{driverName}</td></tr>" +
            $"    <tr><td style='color:#888;padding:4px 0'>Car</td><td style='font-weight:600'>{carModel} · {carPlate}</td></tr>" +
            $"  </table>" +
            $"</div>" +
            $"<p>Please be ready at the pickup point a few minutes early. Have the cash ready for payment.</p>" +
            $"<div style='text-align:center;margin:20px 0'><a href='http://localhost:3000/rider/dashboard' style='background:#185FA5;color:#fff;padding:12px 28px;border-radius:8px;text-decoration:none;font-weight:600'>Open OTW app</a></div>"));

    public Task SendRateRideAsync(string to,string userName,string role,string otherName,int bookingId) =>
        Send(to,userName,"OTW — How was your ride? ⭐", Layout(
            $"<h2 style='color:#1a1a1a'>Hi {userName}, how did it go?</h2>" +
            $"<p>Your ride has been completed. We'd love to hear how it went!</p>" +
            $"<p>Please take a moment to rate your {(role=="Rider"?"driver":"rider")}, <strong>{otherName}</strong>. Your feedback helps keep the OTW community safe and trusted.</p>" +
            $"<div style='text-align:center;margin:24px 0'>" +
            $"  <a href='http://localhost:3000/{role.ToLower()}/rate/{bookingId}' style='background:#EF9F27;color:#fff;padding:12px 28px;border-radius:8px;text-decoration:none;font-weight:600'>⭐ Rate now</a>" +
            $"</div>" +
            $"<p style='color:#888;font-size:12px'>Rating takes less than 30 seconds. It helps other students make better decisions.</p>"));

    public Task SendAdminVerificationReminderAsync(string adminEmail,string driverName,string driverEmail,int userId) =>
        Send(adminEmail,"OTW Admin","OTW — Driver verification reminder", Layout(
            $"<h2 style='color:#1a1a1a'>Verification reminder</h2>" +
            $"<p>Driver <strong>{driverName}</strong> ({driverEmail}) is waiting for account verification and has sent a reminder.</p>" +
            $"<div style='background:#EFF6FF;border-radius:8px;padding:12px 16px;margin:16px 0;font-size:13px'>" +
            $"  <div><span style='color:#888'>Name:</span> <strong>{driverName}</strong></div>" +
            $"  <div style='margin-top:4px'><span style='color:#888'>Email:</span> <strong>{driverEmail}</strong></div>" +
            $"  <div style='margin-top:4px'><span style='color:#888'>User ID:</span> <strong>{userId}</strong></div>" +
            $"</div>" +
            $"<div style='text-align:center;margin:20px 0'><a href='http://localhost:3000/admin/verifications' style='background:#185FA5;color:#fff;padding:12px 28px;border-radius:8px;text-decoration:none;font-weight:600'>Review in admin panel</a></div>"));

    public Task SendWaitlistPromotedAsync(string to,string riderName,string from,string to2,string date,int rideId) =>
        Send(to,riderName,"OTW — A seat just opened for you! 🎉", Layout(
            $"<h2 style='color:#16a36b'>Good news, {riderName}!</h2>" +
            $"<p>A seat has opened on a ride you were waitlisted for. Act quickly before it fills up again!</p>" +
            $"<div style='background:#E1F5EE;border-radius:8px;padding:16px;margin:16px 0;font-size:13px'>" +
            $"  <div><span style='color:#888'>Route:</span> <strong>{from} → {to2}</strong></div>" +
            $"  <div style='margin-top:6px'><span style='color:#888'>Date:</span> <strong>{date}</strong></div>" +
            $"</div>" +
            $"<p>The seat is not automatically reserved — you need to book it now before someone else does.</p>" +
            $"<div style='text-align:center;margin:20px 0'><a href='http://localhost:3000/rider/ride/{rideId}' style='background:#16a36b;color:#fff;padding:12px 28px;border-radius:8px;text-decoration:none;font-weight:600'>Book this seat now</a></div>"));
}

// ── FILE SERVICE (local storage) ─────────────────
public interface IFileService { Task<string> SaveAsync(IFormFile file, string folder); }
public class FileService : IFileService {
    readonly IWebHostEnvironment _env;
    public FileService(IWebHostEnvironment env) => _env = env;
    public async Task<string> SaveAsync(IFormFile file, string folder) {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploadDir = Path.Combine(webRoot, "uploads", folder);
        Directory.CreateDirectory(uploadDir);
        var ext  = Path.GetExtension(file.FileName);
        var name = $"{Guid.NewGuid()}{ext}";
        var path = Path.Combine(uploadDir, name);
        using var stream = new FileStream(path, FileMode.Create);
        await file.CopyToAsync(stream);
        return $"http://localhost:5000/uploads/{folder}/{name}";
    }
}

// ── NOTIFICATION SERVICE ──────────────────────
public interface INotificationService { Task CreateAsync(int userId,string title,string body,string type,int? relatedId=null); }
public class NotificationService : INotificationService {
    readonly AppDbContext _db;
    public NotificationService(AppDbContext db) => _db=db;
    public async Task CreateAsync(int userId,string title,string body,string type,int? relatedId=null) {
        _db.Notifications.Add(new Notification{UserId=userId,Title=title,Body=body,Type=type,RelatedId=relatedId});
        await _db.SaveChangesAsync();
    }
}

// ── AI SERVICE ────────────────────────────────
public interface IAIService { Task<string> ChatAsync(string system, string message); }
public class AIService : IAIService {
    readonly IConfiguration _cfg;
    public AIService(IConfiguration cfg) => _cfg=cfg;
    public async Task<string> ChatAsync(string system, string message) {
        try {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _cfg["OpenAI:ApiKey"]);
            var body = new {
                model = "gpt-3.5-turbo",
                messages = new[] {
                    new { role = "system", content = system },
                    new { role = "user",   content = message }
                },
                max_tokens = 500
            };
            var res  = await http.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", body);
            var json = await res.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!;
        } catch { return "I am unable to process your request right now. Please try again later."; }
    }
}
