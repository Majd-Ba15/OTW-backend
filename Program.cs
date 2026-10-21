using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using OTW.Api.Data;
using OTW.Api.Services;
using OTW.Api.Hubs;
using OTW.Api.Models;

// PostgreSQL (Npgsql) otherwise rejects DateTime values that aren't tagged UTC.
// Legacy mode maps DateTime to a plain timestamp, matching the original SQL Server
// behaviour so dates coming from the frontend (e.g. DepartureTime) keep working.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Render (and most PaaS hosts) assign the listen port dynamically via $PORT.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port)) builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ── Settings ──────────────────────────────────
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));
builder.Services.Configure<MailSettings>(builder.Configuration.GetSection("MailKit"));
builder.Services.Configure<ImgBbSettings>(builder.Configuration.GetSection("ImgBB"));
builder.Services.Configure<BrevoSettings>(builder.Configuration.GetSection("Brevo"));
builder.Services.AddHttpClient();

// ── Database ──────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── JWT Auth ──────────────────────────────────
var jwt = builder.Configuration["AppSettings:JwtSecret"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => {
        o.TokenValidationParameters = new TokenValidationParameters {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwt)),
            ValidateIssuer   = false,
            ValidateAudience = false,
            ClockSkew        = TimeSpan.Zero
        };
        o.Events = new JwtBearerEvents {
            OnMessageReceived = ctx => {
                var t = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(t)) ctx.Token = t;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ── Services ──────────────────────────────────
builder.Services.AddScoped<IAuthService,        AuthService>();
builder.Services.AddScoped<IEmailService,        EmailService>();
builder.Services.AddScoped<IFileService,         FileService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
// Runs every 5 min — sends reminder email 1 hour before each ride
builder.Services.AddHostedService<RideReminderService>();

// ── SignalR ───────────────────────────────────
builder.Services.AddSignalR();

// ── CORS ──────────────────────────────────────
// CORS_ALLOWED_ORIGINS is a comma-separated env var (e.g. your Vercel URL) that
// extends the default localhost origin used for local frontend development.
var corsOrigins = (Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS") ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Append("http://localhost:3000")          // local frontend dev
    .Append("https://otw-fyp.vercel.app")     // deployed Vercel frontend
    .Distinct()
    .ToArray();

builder.Services.AddCors(o => o.AddPolicy("OTWPolicy", p =>
    p.WithOrigins(corsOrigins)
     .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── Static files (uploads) ────────────────────
builder.Services.AddDirectoryBrowser();

var app = builder.Build();

// ── Auto-seed admin on startup ────────────────
using (var scope = app.Services.CreateScope()) {
    var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var cfg = builder.Configuration;
    db.Database.EnsureCreated();
    db.Database.ExecuteSqlRaw(@"
DROP TABLE IF EXISTS ""Waitlist"";
ALTER TABLE ""RideRequests"" ADD COLUMN IF NOT EXISTS ""FromLat"" decimal(10,7);
ALTER TABLE ""RideRequests"" ADD COLUMN IF NOT EXISTS ""FromLng"" decimal(10,7);
ALTER TABLE ""RideRequests"" ADD COLUMN IF NOT EXISTS ""ToLat"" decimal(10,7);
ALTER TABLE ""RideRequests"" ADD COLUMN IF NOT EXISTS ""ToLng"" decimal(10,7);
");
    var adminEmail = cfg["AppSettings:AdminEmail"]!;
    if (!db.Users.Any(u => u.Email == adminEmail)) {
        db.Users.Add(new User {
            FullName          = "OTW Admin",
            Email             = adminEmail,
            PasswordHash      = BCrypt.Net.BCrypt.HashPassword(cfg["AppSettings:AdminPassword"]!),
            Role              = "Admin",
            IsEmailVerified   = true,
            IsVerified        = true,
            IsActive          = true,
            ProfileCompletion = 100,
            CreatedAt         = DateTime.UtcNow
        });
        db.SaveChanges();
        Console.WriteLine($"Admin seeded: {adminEmail}");
    }
}

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.UseStaticFiles(new StaticFileOptions {
    // Stop browsers from MIME-sniffing user-uploaded files into executable
    // content (defence-in-depth alongside the image validation in FileService).
    OnPrepareResponse = ctx => ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff"
});
app.UseCors("OTWPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();

