using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OTW.Api.Data;
using OTW.Api.Models;

namespace OTW.Api.Hubs;

[Authorize]
public class ChatHub : Hub {
    readonly AppDbContext _db;
    public ChatHub(AppDbContext db) => _db=db;
    int Me => int.Parse(Context.User!.FindFirst("userId")!.Value);

    public async Task JoinRideGroup(int rideId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"ride_{rideId}");

    public async Task LeaveRideGroup(int rideId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ride_{rideId}");

    public async Task SendMessage(int rideId, int receiverId, string content) {
        var msg = new Message { RideId=rideId, SenderId=Me, ReceiverId=receiverId, Content=content, SentAt=DateTime.UtcNow };
        _db.Messages.Add(msg); await _db.SaveChangesAsync();
        await Clients.Group($"ride_{rideId}").SendAsync("ReceiveMessage", new {
            msg.MessageId, msg.SenderId, msg.ReceiverId, msg.Content, msg.SentAt, msg.IsBroadcast
        });
    }

    public async Task BroadcastMessage(int rideId, string content) {
        var msg = new Message { RideId=rideId, SenderId=Me, Content=content, IsBroadcast=true, SentAt=DateTime.UtcNow };
        _db.Messages.Add(msg); await _db.SaveChangesAsync();
        await Clients.Group($"ride_{rideId}").SendAsync("ReceiveMessage", new {
            msg.MessageId, msg.SenderId, msg.Content, msg.SentAt, msg.IsBroadcast
        });
    }

    // Live location — driver sends GPS every 3 seconds
    public async Task UpdateLocation(int rideId, decimal lat, decimal lng) {
        var ride = await _db.Rides.FindAsync(rideId);
        if (ride != null && ride.DriverId == Me) {
            ride.CurrentLat = lat; ride.CurrentLng = lng;
            _db.LocationHistory.Add(new LocationHistory { RideId=rideId, DriverId=Me, Lat=lat, Lng=lng });
            await _db.SaveChangesAsync();
            await Clients.Group($"ride_{rideId}").SendAsync("LocationUpdated", new { lat, lng, timestamp=DateTime.UtcNow });
        }
    }

    public async Task Typing(int rideId) =>
        await Clients.OthersInGroup($"ride_{rideId}").SendAsync("UserTyping", Me);

    public async Task SendNotificationToUser(int userId, string title, string body) =>
        await Clients.User(userId.ToString()).SendAsync("ReceiveNotification", new { title, body });
}
