using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTW.Api.Data;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

[ApiController][Route("api/messages")][Authorize]
public class MessagesController : ControllerBase {
    readonly AppDbContext _db;
    public MessagesController(AppDbContext db) => _db=db;
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    [HttpGet("{rideId}")]
    public async Task<IActionResult> Get(int rideId) =>
        Ok(await _db.Messages.Include(m=>m.Sender).Where(m=>m.RideId==rideId&&(m.SenderId==Me||m.ReceiverId==Me||m.IsBroadcast)).OrderBy(m=>m.SentAt).Select(m=>new{m.MessageId,m.SenderId,m.ReceiverId,m.Content,m.SentAt,m.IsBroadcast,m.IsRead,Sender=new{m.Sender.UserId,m.Sender.FullName,m.Sender.ProfilePhoto}}).ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendMessageRequest req) {
        if (string.IsNullOrWhiteSpace(req.Content)) return BadRequest(new { message = "Message content is required" });

        var ride = await _db.Rides.Include(r => r.Bookings).FirstOrDefaultAsync(r => r.RideId == req.RideId);
        if (ride == null) return NotFound(new { message = "Ride not found" });

        var isParticipant = ride.DriverId == Me || ride.Bookings.Any(b => b.RiderId == Me && (b.Status == "Confirmed" || b.Status == "Pending"));
        if (!isParticipant) return Forbid();

        if (!req.IsBroadcast && req.ReceiverId == null) return BadRequest(new { message = "receiverId is required for private messages" });
        if (!req.IsBroadcast && req.ReceiverId != ride.DriverId && !ride.Bookings.Any(b => b.RiderId == req.ReceiverId && (b.Status == "Confirmed" || b.Status == "Pending")))
            return BadRequest(new { message = "Receiver does not belong to this ride" });

        var msg = new Message {
            RideId = req.RideId,
            SenderId = Me,
            ReceiverId = req.IsBroadcast ? null : req.ReceiverId,
            Content = req.Content.Trim(),
            IsBroadcast = req.IsBroadcast,
            SentAt = DateTime.UtcNow
        };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();
        return Ok(new { messageId = msg.MessageId, sentAt = msg.SentAt });
    }

    [HttpPost("broadcast")]
    public Task<IActionResult> Broadcast([FromBody] SendMessageRequest req) =>
        Send(req with { IsBroadcast = true, ReceiverId = null });
}
