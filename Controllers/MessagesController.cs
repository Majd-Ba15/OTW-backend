using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OTW.Api.Data;
using OTW.Api.Hubs;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

[ApiController][Route("api/messages")][Authorize]
public class MessagesController : ControllerBase {
    readonly AppDbContext _db;
    readonly IHubContext<ChatHub> _hub;
    public MessagesController(AppDbContext db, IHubContext<ChatHub> hub) { _db=db; _hub=hub; }
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    [HttpGet("{rideId}")]
    public async Task<IActionResult> Get(int rideId) =>
        Ok(await _db.Messages.Include(m=>m.Sender).Where(m=>m.RideId==rideId&&(m.SenderId==Me||m.ReceiverId==Me||m.IsBroadcast)).OrderBy(m=>m.SentAt).Select(m=>new{m.MessageId,m.SenderId,m.ReceiverId,m.Content,m.SentAt,m.IsBroadcast,m.IsRead,Sender=new{m.Sender.UserId,m.Sender.FullName,m.Sender.ProfilePhoto}}).ToListAsync());

    // One row per (ride, other person) the current user has exchanged messages
    // with — powers the "Chats" tab. Bucketed in memory since chat volume for
    // this app is small; broadcasts are keyed to the driver who sent them.
    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations() {
        var driverRideIds = await _db.Rides.Where(r=>r.DriverId==Me).Select(r=>r.RideId).ToListAsync();
        var riderRideIds  = await _db.Bookings.Where(b=>b.RiderId==Me&&(b.Status=="Confirmed"||b.Status=="Pending"||b.Status=="Completed")).Select(b=>b.RideId).Distinct().ToListAsync();
        var myRideIds = driverRideIds.Union(riderRideIds).ToList();
        if(myRideIds.Count==0) return Ok(new List<object>());

        var messages = await _db.Messages.Where(m=>myRideIds.Contains(m.RideId)&&(m.SenderId==Me||m.ReceiverId==Me||m.IsBroadcast)).OrderBy(m=>m.SentAt).ToListAsync();

        var buckets = new Dictionary<(int rideId,int otherUserId),List<Message>>();
        foreach(var m in messages) {
            int otherUserId;
            if(m.IsBroadcast) {
                if(m.SenderId==Me) continue; // my own broadcast isn't a conversation with myself
                otherUserId = m.SenderId;    // the driver who sent it
            } else {
                otherUserId = m.SenderId==Me ? (m.ReceiverId??0) : m.SenderId;
            }
            if(otherUserId==0) continue;
            var key=(m.RideId,otherUserId);
            if(!buckets.TryGetValue(key,out var list)) buckets[key]=list=new List<Message>();
            list.Add(m);
        }

        var otherUserIds = buckets.Keys.Select(k=>k.otherUserId).Distinct().ToList();
        var users = await _db.Users.Where(u=>otherUserIds.Contains(u.UserId)).ToDictionaryAsync(u=>u.UserId);
        var rides = await _db.Rides.Where(r=>myRideIds.Contains(r.RideId)).ToDictionaryAsync(r=>r.RideId);

        var result = buckets.Select(kv => {
            var (rideId,otherUserId) = kv.Key;
            var last   = kv.Value[^1];
            var user   = users.GetValueOrDefault(otherUserId);
            var ride   = rides.GetValueOrDefault(rideId);
            var unread = kv.Value.Count(m=>m.SenderId!=Me&&!m.IsRead);
            var name   = user?.FullName ?? "Unknown";
            return new {
                rideId,
                otherUserId,
                otherUserName     = name,
                otherUserInitials = string.Concat(name.Split(' ',StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p=>p[0])).ToUpper(),
                lastMessage        = last.Content,
                lastMessageAt      = last.SentAt,
                unreadCount        = unread,
                route              = ride!=null ? $"{ride.FromLocation} → {ride.ToLocation}" : "",
            };
        }).OrderByDescending(c=>c.lastMessageAt).ToList();

        return Ok(result);
    }

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

        // Push to anyone else currently viewing this ride's chat — mirrors the
        // REST-write-then-SignalR-broadcast pattern used for location updates.
        var sender = await _db.Users.FindAsync(Me);
        await _hub.Clients.Group($"ride_{req.RideId}").SendAsync("ReceiveMessage", new {
            msg.MessageId, msg.SenderId, msg.ReceiverId, msg.Content, msg.SentAt, msg.IsBroadcast,
            senderName = sender?.FullName
        });

        return Ok(new { messageId = msg.MessageId, sentAt = msg.SentAt });
    }

    [HttpPost("broadcast")]
    public Task<IActionResult> Broadcast([FromBody] SendMessageRequest req) =>
        Send(req with { IsBroadcast = true, ReceiverId = null });
}
