using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTW.Api.Data;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

[ApiController][Route("api/notifications")][Authorize]
public class NotificationsController : ControllerBase {
    readonly AppDbContext _db;
    public NotificationsController(AppDbContext db) => _db=db;
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _db.Notifications.Where(n=>n.UserId==Me).OrderByDescending(n=>n.CreatedAt).ToListAsync());

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount() =>
        Ok(new{count=await _db.Notifications.CountAsync(n=>n.UserId==Me&&!n.IsRead)});

    [HttpPut("read-all")]
    public async Task<IActionResult> ReadAll() {
        var notifs=await _db.Notifications.Where(n=>n.UserId==Me&&!n.IsRead).ToListAsync();
        notifs.ForEach(n=>n.IsRead=true); await _db.SaveChangesAsync(); return Ok();
    }

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkRead(int id) {
        var n=await _db.Notifications.FirstOrDefaultAsync(x=>x.NotificationId==id&&x.UserId==Me);
        if(n==null) return NotFound(); n.IsRead=true; await _db.SaveChangesAsync(); return Ok();
    }
}
