using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using OTW.Api.Data;
using OTW.Api.Services;
using OTW.Api.Hubs;
using OTW.Api.Models;

var builder = WebApplication.CreateBuilder(args);

// â”€â”€ Settings â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));
builder.Services.Configure<MailSettings>(builder.Configuration.GetSection("MailKit"));

// â”€â”€ Database â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// â”€â”€ JWT Auth â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

// â”€â”€ Services â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
builder.Services.AddScoped<IAuthService,        AuthService>();
builder.Services.AddScoped<IEmailService,        EmailService>();
builder.Services.AddScoped<IFileService,         FileService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAIService,           AIService>();
// Runs every 5 min â€” sends reminder email 1 hour before each ride
builder.Services.AddHostedService<RideReminderService>();

// â”€â”€ SignalR â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
builder.Services.AddSignalR();

// â”€â”€ CORS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
builder.Services.AddCors(o => o.AddPolicy("OTWPolicy", p =>
    p.WithOrigins("http://localhost:3000")
     .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// â”€â”€ Static files (uploads) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
builder.Services.AddDirectoryBrowser();

var app = builder.Build();

// â”€â”€ Auto-seed admin on startup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
using (var scope = app.Services.CreateScope()) {
    var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var cfg = builder.Configuration;
    db.Database.EnsureCreated();
    db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID('dbo.Waitlist', 'U') IS NOT NULL
    DROP TABLE dbo.Waitlist;

IF COL_LENGTH('dbo.RideRequests', 'FromLat') IS NULL
    ALTER TABLE dbo.RideRequests ADD FromLat decimal(10,7) NULL;

IF COL_LENGTH('dbo.RideRequests', 'FromLng') IS NULL
    ALTER TABLE dbo.RideRequests ADD FromLng decimal(10,7) NULL;

IF COL_LENGTH('dbo.RideRequests', 'ToLat') IS NULL
    ALTER TABLE dbo.RideRequests ADD ToLat decimal(10,7) NULL;

IF COL_LENGTH('dbo.RideRequests', 'ToLng') IS NULL
    ALTER TABLE dbo.RideRequests ADD ToLng decimal(10,7) NULL;
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

app.UseStaticFiles();
app.UseCors("OTWPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();

