using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTW.Api.Data;
using OTW.Api.Models;

namespace OTW.Api.Controllers;

[ApiController][Route("api/chat")][Authorize]
public class ChatAIController : ControllerBase {
    readonly AppDbContext _db;
    public ChatAIController(AppDbContext db) { _db=db; }
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory() =>
        Ok(await _db.ChatLogs.Where(l=>l.UserId==Me).OrderByDescending(l=>l.CreatedAt).Take(20).ToListAsync());

    [HttpPost("suggestions")]
    public async Task<IActionResult> GetSuggestions() {
        var patterns=await _db.Bookings.Include(b=>b.Ride).Where(b=>b.RiderId==Me&&b.Status=="Completed").OrderByDescending(b=>b.CreatedAt).Take(10).Select(b=>new{b.Ride.FromLocation,b.Ride.ToLocation}).ToListAsync();
        if(!patterns.Any()) return Ok(new{suggestions=new List<object>()});
        var common=patterns.GroupBy(p=>new{p.FromLocation,p.ToLocation}).OrderByDescending(g=>g.Count()).First();
        var suggestions=await _db.Rides.Include(r=>r.Driver).Where(r=>r.Status=="Upcoming"&&r.AvailableSeats>0&&r.FromLocation.Contains(common.Key.FromLocation)&&r.ToLocation.Contains(common.Key.ToLocation)&&r.DepartureTime>DateTime.UtcNow).Take(3).Select(r=>new{r.RideId,r.FromLocation,r.ToLocation,r.DepartureTime,r.AvailableSeats,r.PricePerSeat,Driver=new{r.Driver.FullName,r.Driver.AverageRating}}).ToListAsync();
        return Ok(new{suggestions,pattern=common.Key});
    }
}
