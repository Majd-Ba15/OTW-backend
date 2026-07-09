using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OTW.Api.Data;
using OTW.Api.Models;
using OTW.Api.Services;
using OTW.Api.Constants;

namespace OTW.Api.Controllers;

[ApiController][Route("api/auth")]
public class AuthController : ControllerBase {
    readonly IAuthService  _auth;
    readonly AppSettings   _cfg;
    public AuthController(IAuthService auth, IOptions<AppSettings> cfg) { _auth=auth; _cfg=cfg.Value; }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req) {
        if (_cfg.RequireUniversityEmail) {
            var domain = req.Email.Split('@').LastOrDefault() ?? "";
            if (!_cfg.AllowedEmailDomains.Contains(domain))
                return BadRequest(new { message="University email required" });
        }
        // Per-university domain check: OTHER (or an unrecognised code) always
        // passes; a real university requires the email domain to match.
        if (!Universities.EmailMatches(req.Email, req.University))
            return BadRequest(new { message="Your email domain does not match the selected university. Use your university email or select Other." });
        var r = await _auth.RegisterAsync(req);
        return r.Success ? Ok(new{message=r.Message,userId=r.UserId,requiresOtp=_cfg.RequireOTP,token=r.Token,role=r.Role,name=r.FullName}) : BadRequest(new{message=r.Message});
    }
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpRequest req) {
        var r = await _auth.VerifyOtpAsync(req.UserId,req.OtpCode);
        return r.Success ? Ok(new{message="Email verified",token=r.Token,userId=r.UserId,role=r.Role,name=r.FullName}) : BadRequest(new{message=r.Message});
    }
    [HttpPost("resend-otp")]
    public async Task<IActionResult> ResendOtp([FromBody] ResendOtpRequest req) {
        var r = await _auth.ResendOtpAsync(req.UserId);
        return r.Success ? Ok() : BadRequest(new{message=r.Message});
    }
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req) {
        var r = await _auth.LoginAsync(req.Email,req.Password);
        return r.Success ? Ok(new{token=r.Token,userId=r.UserId,role=r.Role,name=r.FullName}) : Unauthorized(new{message=r.Message});
    }
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req) {
        await _auth.ForgotPasswordAsync(req.Email);
        return Ok(new{message="If this email exists a reset link has been sent"});
    }
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromQuery] string token,[FromBody] ResetPasswordRequest req) {
        var r = await _auth.ResetPasswordAsync(token,req.Password);
        return r.Success ? Ok(new{message="Password reset"}) : BadRequest(new{message=r.Message});
    }
}
