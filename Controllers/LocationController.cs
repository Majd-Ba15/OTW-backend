using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OTW.Api.Data;
using OTW.Api.Hubs;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

[ApiController][Route("api/location")][Authorize]
public class LocationController : ControllerBase {
    readonly AppDbContext _db;
    readonly IHubContext<ChatHub> _hub;
    public LocationController(AppDbContext db, IHubContext<ChatHub> hub) { _db=db; _hub=hub; }
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    [HttpPut("update")]
    public async Task<IActionResult> Update([FromBody] LocationUpdate req) {
        var ride=await _db.Rides.FindAsync(req.RideId);
        if(ride==null||ride.DriverId!=Me) return Forbid();
        ride.CurrentLat=req.Lat; ride.CurrentLng=req.Lng;
        _db.LocationHistory.Add(new LocationHistory{RideId=req.RideId,DriverId=Me,Lat=req.Lat,Lng=req.Lng});
        await _db.SaveChangesAsync();
        // Push the new position to anyone (the rider) subscribed to this ride's group.
        await _hub.Clients.Group($"ride_{req.RideId}").SendAsync("LocationUpdated", new { lat=req.Lat, lng=req.Lng, timestamp=DateTime.UtcNow });
        return Ok();
    }

    [HttpGet("history/{rideId}")]
    public async Task<IActionResult> GetHistory(int rideId) =>
        Ok(await _db.LocationHistory.Where(l=>l.RideId==rideId).OrderBy(l=>l.RecordedAt).Select(l=>new{l.Lat,l.Lng,l.RecordedAt}).ToListAsync());
}
