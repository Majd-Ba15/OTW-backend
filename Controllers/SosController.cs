using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTW.Api.Data;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

[ApiController][Route("api/sos")][Authorize]
public class SosController : ControllerBase {
    readonly AppDbContext _db;
    readonly IEmailService _email;
    public SosController(AppDbContext db, IEmailService email) { _db=db; _email=email; }
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    // POST /api/sos/alert — emails one trusted contact with the user's live location.
    // Called once per contact by the SOS page. MapsUrl is null when GPS is unavailable.
    [HttpPost("alert")]
    public async Task<IActionResult> Alert([FromBody] SosAlertRequest req) {
        if (string.IsNullOrWhiteSpace(req.ToEmail))
            return BadRequest(new { message = "Recipient email is required" });

        // Trust the authenticated user's real name over anything the client sends.
        var me = await _db.Users.FirstOrDefaultAsync(u => u.UserId == Me);
        var fromName = !string.IsNullOrWhiteSpace(me?.FullName) ? me!.FullName
                     : (!string.IsNullOrWhiteSpace(req.FromName) ? req.FromName : "An OTW user");

        var mapsUrl = string.IsNullOrWhiteSpace(req.MapsUrl) ? null : req.MapsUrl.Trim();

        await _email.SendSosAlertAsync(req.ToEmail.Trim(), req.ToName ?? "", fromName, mapsUrl);
        return Ok(new { sent = true });
    }
}
