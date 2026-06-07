using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTW.Api.Data;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

// ════════════════════════════════════════════
// MESSAGES
// ════════════════════════════════════════════
[ApiController][Route("api/messages")][Authorize]
public class MessagesController : ControllerBase {
    readonly AppDbContext _db;
    public MessagesController(AppDbContext db) => _db=db;
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    [HttpGet("{rideId}")]
    public async Task<IActionResult> Get(int rideId) =>
        Ok(await _db.Messages.Include(m=>m.Sender).Where(m=>m.RideId==rideId&&(m.SenderId==Me||m.ReceiverId==Me||m.IsBroadcast)).OrderBy(m=>m.SentAt).Select(m=>new{m.MessageId,m.Content,m.SentAt,m.IsBroadcast,m.IsRead,Sender=new{m.Sender.UserId,m.Sender.FullName,m.Sender.ProfilePhoto}}).ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] Message msg) {
        msg.SenderId=Me; msg.SentAt=DateTime.UtcNow;
        _db.Messages.Add(msg); await _db.SaveChangesAsync();
        return Ok(new{messageId=msg.MessageId});
    }
}

// ════════════════════════════════════════════
// RATINGS
// ════════════════════════════════════════════
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

// ════════════════════════════════════════════
// NOTIFICATIONS
// ════════════════════════════════════════════
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

// ════════════════════════════════════════════
// REPORTS
// ════════════════════════════════════════════
[ApiController][Route("api/reports")][Authorize]
public class ReportsController : ControllerBase {
    readonly AppDbContext _db;
    public ReportsController(AppDbContext db) => _db=db;
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    [HttpPost]
    public async Task<IActionResult> File([FromBody] ReportRequest req) {
        var r=new Report{ReportedBy=Me,ReportedUser=req.ReportedUser,RideId=req.RideId,Type=req.Type,Statement=req.Statement,Status="Open"};
        _db.Reports.Add(r); await _db.SaveChangesAsync();
        return Ok(new{reportId=r.ReportId});
    }

    [HttpGet("mine")]
    public async Task<IActionResult> GetMine() =>
        Ok(await _db.Reports.Where(r=>r.ReportedBy==Me).OrderByDescending(r=>r.CreatedAt).ToListAsync());
}

// ════════════════════════════════════════════
// AI CHAT
// ════════════════════════════════════════════
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

// ════════════════════════════════════════════
// LOCATION CONTROLLER
// ════════════════════════════════════════════
[ApiController][Route("api/location")][Authorize]
public class LocationController : ControllerBase {
    readonly AppDbContext _db;
    public LocationController(AppDbContext db) => _db=db;
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    [HttpPut("update")]
    public async Task<IActionResult> Update([FromBody] LocationUpdate req) {
        var ride=await _db.Rides.FindAsync(req.RideId);
        if(ride==null||ride.DriverId!=Me) return Forbid();
        ride.CurrentLat=req.Lat; ride.CurrentLng=req.Lng;
        _db.LocationHistory.Add(new LocationHistory{RideId=req.RideId,DriverId=Me,Lat=req.Lat,Lng=req.Lng});
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("history/{rideId}")]
    public async Task<IActionResult> GetHistory(int rideId) =>
        Ok(await _db.LocationHistory.Where(l=>l.RideId==rideId).OrderBy(l=>l.RecordedAt).Select(l=>new{l.Lat,l.Lng,l.RecordedAt}).ToListAsync());
}

// ════════════════════════════════════════════
// ADMIN CONTROLLER
// ════════════════════════════════════════════
[ApiController][Route("api/admin")][Authorize(Roles="Admin")]
public class AdminController : ControllerBase {
    readonly AppDbContext  _db;
    readonly IEmailService _email;
    public AdminController(AppDbContext db, IEmailService email) { _db=db; _email=email; }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats() =>
        Ok(new{TotalUsers=await _db.Users.CountAsync(u=>u.Role!="Admin"),ActiveRides=await _db.Rides.CountAsync(r=>r.Status=="Active"||r.Status=="Upcoming"),PendingVerifs=await _db.Users.CountAsync(u=>!u.IsVerified&&u.Role!="Admin"&&u.StudentIdPhoto!=null),OpenReports=await _db.Reports.CountAsync(r=>r.Status=="Open"),TotalRides=await _db.Rides.CountAsync(),TotalBookings=await _db.Bookings.CountAsync(b=>b.Status=="Completed")});

    [HttpGet("activity")]
    public async Task<IActionResult> GetActivity() {
        var users=await _db.Users.OrderByDescending(u=>u.CreatedAt).Take(5).Select(u=>new{type="user",u.FullName,u.Email,u.CreatedAt}).ToListAsync();
        var rides=await _db.Rides.Include(r=>r.Driver).OrderByDescending(r=>r.CreatedAt).Take(5).Select(r=>new{type="ride",r.Driver.FullName,r.FromLocation,r.ToLocation,r.CreatedAt}).ToListAsync();
        return Ok(new{users,rides});
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery]string? status,[FromQuery]string? role,[FromQuery]string? search) {
        var q=_db.Users.Where(u=>u.Role!="Admin");
        if(status=="verified")  q=q.Where(u=>u.IsVerified&&u.IsActive);
        if(status=="pending")   q=q.Where(u=>!u.IsVerified&&u.IsActive);
        if(status=="suspended") q=q.Where(u=>!u.IsActive);
        if(!string.IsNullOrEmpty(role))   q=q.Where(u=>u.Role==role);
        if(!string.IsNullOrEmpty(search)) q=q.Where(u=>u.FullName.Contains(search)||u.Email.Contains(search)||(u.StudentId!=null&&u.StudentId.Contains(search)));
        return Ok(await q.OrderByDescending(u=>u.CreatedAt).ToListAsync());
    }

    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUser(int id) {
        var user=await _db.Users.FindAsync(id); if(user==null) return NotFound();
        var rides=await _db.Bookings.CountAsync(b=>b.RiderId==id&&b.Status=="Completed");
        var reports=await _db.Reports.CountAsync(r=>r.ReportedUser==id);
        var recent=await _db.Bookings.Include(b=>b.Ride).Where(b=>b.RiderId==id).OrderByDescending(b=>b.CreatedAt).Take(5).ToListAsync();
        return Ok(new{user,rideCount=rides,reportCount=reports,recentRides=recent});
    }

    [HttpGet("verifications/pending")]
    public async Task<IActionResult> GetPending() =>
        Ok(await _db.Users.Where(u=>!u.IsVerified&&u.StudentIdPhoto!=null&&u.Role!="Admin").OrderBy(u=>u.CreatedAt).ToListAsync());

    [HttpPut("verifications/{id}/approve")]
    public async Task<IActionResult> Approve(int id) {
        var u=await _db.Users.FindAsync(id); if(u==null) return NotFound();
        u.IsVerified=true; await _db.SaveChangesAsync();
        await _email.SendVerificationApprovedAsync(u.Email,u.FullName);
        return Ok(new{message="User approved"});
    }

    [HttpPut("verifications/{id}/reject")]
    public async Task<IActionResult> Reject(int id,[FromBody] AdminActionRequest req) {
        var u=await _db.Users.FindAsync(id); if(u==null) return NotFound();
        await _email.SendVerificationRejectedAsync(u.Email,u.FullName,req.Reason??"No reason provided");
        return Ok(new{message="User rejected"});
    }

    [HttpPut("users/{id}/warn")]
    public async Task<IActionResult> Warn(int id,[FromBody] AdminActionRequest req) {
        var u=await _db.Users.FindAsync(id); if(u==null) return NotFound();
        await _email.SendWarningAsync(u.Email,u.FullName,req.Note??"");
        return Ok(new{message="Warning sent"});
    }

    [HttpPut("users/{id}/suspend")]
    public async Task<IActionResult> Suspend(int id) {
        var u=await _db.Users.FindAsync(id); if(u==null) return NotFound();
        u.IsActive=false; await _db.SaveChangesAsync(); return Ok(new{message="User suspended"});
    }

    [HttpDelete("users/{id}/ban")]
    public async Task<IActionResult> Ban(int id) {
        var u=await _db.Users.FindAsync(id); if(u==null) return NotFound();
        u.IsActive=false; u.IsVerified=false; await _db.SaveChangesAsync(); return Ok(new{message="User banned"});
    }

    [HttpGet("rides")]
    public async Task<IActionResult> GetRides([FromQuery]string? status) {
        var q=_db.Rides.Include(r=>r.Driver).AsQueryable();
        if(!string.IsNullOrEmpty(status)) q=q.Where(r=>r.Status==status);
        return Ok(await q.OrderByDescending(r=>r.CreatedAt).ToListAsync());
    }

    [HttpGet("reports")]
    public async Task<IActionResult> GetReports([FromQuery]string? status) {
        var q=_db.Reports.AsQueryable();
        if(!string.IsNullOrEmpty(status)) q=q.Where(r=>r.Status==status);
        return Ok(await q.OrderByDescending(r=>r.CreatedAt).ToListAsync());
    }

    [HttpGet("reports/{id}")]
    public async Task<IActionResult> GetReport(int id) {
        var r=await _db.Reports.FindAsync(id); if(r==null) return NotFound();
        var reporter=await _db.Users.FindAsync(r.ReportedBy);
        var reported=await _db.Users.FindAsync(r.ReportedUser);
        return Ok(new{report=r,reporter,reported});
    }

    [HttpPut("reports/{id}/resolve")]
    public async Task<IActionResult> Resolve(int id,[FromBody] AdminActionRequest req) {
        var r=await _db.Reports.FindAsync(id); if(r==null) return NotFound();
        r.Status="Resolved"; r.AdminNote=req.Note; r.ActionTaken=req.Reason; r.ResolvedAt=DateTime.UtcNow;
        await _db.SaveChangesAsync(); return Ok(new{message="Report resolved"});
    }

    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics() {
        var ridesPerDay=await _db.Rides.Where(r=>r.CreatedAt>=DateTime.UtcNow.AddDays(-7)).GroupBy(r=>r.CreatedAt.Date).Select(g=>new{date=g.Key,count=g.Count()}).ToListAsync();
        var topRoutes=await _db.Rides.GroupBy(r=>new{r.FromLocation,r.ToLocation}).Select(g=>new{g.Key.FromLocation,g.Key.ToLocation,count=g.Count()}).OrderByDescending(x=>x.count).Take(5).ToListAsync();
        var totalUsers=await _db.Users.CountAsync(u=>u.Role!="Admin");
        var newUsersThisWeek=await _db.Users.CountAsync(u=>u.CreatedAt>=DateTime.UtcNow.AddDays(-7));
        var successRate=await _db.Bookings.CountAsync()>0?(await _db.Bookings.CountAsync(b=>b.Status=="Completed")*100.0/await _db.Bookings.CountAsync()):0;
        return Ok(new{ridesPerDay,topRoutes,totalUsers,newUsersThisWeek,successRate=Math.Round(successRate,1)});
    }
}
