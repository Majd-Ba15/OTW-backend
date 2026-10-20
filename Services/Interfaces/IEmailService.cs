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
    Task SendAdminVerificationReminderAsync(string adminEmail, string driverName, string driverEmail, int userId);
    // SOS emergency alert to a rider's trusted contact. mapsUrl is null when no live location.
    Task SendSosAlertAsync(string to, string toName, string fromName, string? mapsUrl);
    // Admin nudges a matching driver about an open rider request (demand planning).
    Task SendDemandNotifyAsync(string to, string driverName, string from, string to2, string date, int seats);
}

