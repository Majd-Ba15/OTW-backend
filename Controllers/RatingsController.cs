using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTW.Api.Data;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

[ApiController][Route("api/ratings")][Authorize]
public class RatingsController : ControllerBase {
    readonly AppDbContext _db;
    public RatingsController(AppDbContext db) => _db=db;
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    [HttpPost]
    public async Task<IActionResult> Rate([FromBody] RatingRequest req) {
        if(await _db.Ratings.AnyAsync(r=>r.BookingId==req.BookingId&&r.RaterId==Me))
            return BadRequest(new{message="Already rated"});
        _db.Ratings.Add(new Rating{BookingId=req.BookingId,RaterId=Me,RatedUserId=req.RatedUserId,Stars=req.Stars,Tags=req.Tags,Comment=req.Comment});
        await _db.SaveChangesAsync();
        var avg=await _db.Ratings.Where(r=>r.RatedUserId==req.RatedUserId).AverageAsync(r=>(double)r.Stars);
        var user=await _db.Users.FindAsync(req.RatedUserId);
        if(user!=null){user.AverageRating=(decimal)avg;await _db.SaveChangesAsync();}
        return Ok(new{message="Rating submitted"});
    }

    [HttpGet("user/{id}")]
    public async Task<IActionResult> GetUserRatings(int id) =>
        Ok(await _db.Ratings.Where(r=>r.RatedUserId==id).OrderByDescending(r=>r.CreatedAt).ToListAsync());
}
