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

public interface IAuthService {
    Task<ServiceResult> RegisterAsync(RegisterRequest req);
    Task<ServiceResult> VerifyOtpAsync(int userId, string otp);
    Task<ServiceResult> ResendOtpAsync(int userId);
    Task<ServiceResult> LoginAsync(string email, string password);
    Task<ServiceResult> ForgotPasswordAsync(string email);
    Task<ServiceResult> ResetPasswordAsync(string token, string newPass);
}

