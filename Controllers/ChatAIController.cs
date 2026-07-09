using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTW.Api.Data;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

[ApiController][Route("api/chat")][Authorize]
public class ChatAIController : ControllerBase {
    readonly AppDbContext _db;
    readonly IAIService   _ai;
    public ChatAIController(AppDbContext db, IAIService ai) { _db=db; _ai=ai; }
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    [HttpPost("support")]
    public async Task<IActionResult> Support([FromBody] ChatRequest req) {
        var user=await _db.Users.FindAsync(Me); if(user==null) return NotFound();
        var sys=user.Role=="Driver"
            ?"You are OTW support for drivers on a university carpooling app. Only answer questions about posting rides, bookings, verification, car details, ratings, cancellations. Be short and helpful."
            :"You are OTW support for riders on a university carpooling app. Only answer questions about finding rides, booking, cancellations, cash payment, chat, ratings, safety, SOS. Be short and helpful.";
        var reply=await _ai.ChatAsync(sys,req.Message);
        _db.ChatLogs.Add(new ChatLog{UserId=Me,UserMessage=req.Message,AIResponse=reply});
        await _db.SaveChangesAsync();
        return Ok(new{reply});
    }

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
