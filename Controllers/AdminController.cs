using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTW.Api.Data;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

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
        if(string.IsNullOrEmpty(status)) q=q.Where(u=>u.IsActive);
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
    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] AdminUserUpdateRequest req) {
        var u=await _db.Users.FindAsync(id); if(u==null) return NotFound();
        if(!string.IsNullOrWhiteSpace(req.FullName)) u.FullName=req.FullName.Trim();
        if(!string.IsNullOrWhiteSpace(req.Email)) u.Email=req.Email.Trim();
        u.StudentId=string.IsNullOrWhiteSpace(req.StudentId)?null:req.StudentId.Trim();
        u.Faculty=string.IsNullOrWhiteSpace(req.Faculty)?null:req.Faculty.Trim();
        u.Phone=string.IsNullOrWhiteSpace(req.Phone)?null:req.Phone.Trim();
        u.Gender=string.IsNullOrWhiteSpace(req.Gender)?null:req.Gender.Trim();
        if(!string.IsNullOrWhiteSpace(req.Role)) u.Role=req.Role.Trim();
        u.University=string.IsNullOrWhiteSpace(req.University)?null:req.University.Trim();
        u.CampusName=string.IsNullOrWhiteSpace(req.CampusName)?null:req.CampusName.Trim();
        if(req.IsActive.HasValue) u.IsActive=req.IsActive.Value;
        if(req.IsVerified.HasValue) u.IsVerified=req.IsVerified.Value;
        if(req.IsEmailVerified.HasValue) u.IsEmailVerified=req.IsEmailVerified.Value;
        await _db.SaveChangesAsync();
        return Ok(new{message="User updated",user=u});
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
    public async Task<IActionResult> GetAnalytics([FromQuery]int days=7) {
        days=Math.Clamp(days,1,365);
        var now=DateTime.UtcNow; var from=now.AddDays(-days); var prevFrom=now.AddDays(-2*days);

        // ── Series & routes (selected window) ──
        var ridesPerDay=await _db.Rides.Where(r=>r.CreatedAt>=from).GroupBy(r=>r.CreatedAt.Date).Select(g=>new{date=g.Key,count=g.Count()}).OrderBy(x=>x.date).ToListAsync();
        var topRoutes=await _db.Rides.Where(r=>r.CreatedAt>=from).GroupBy(r=>new{r.FromLocation,r.ToLocation}).Select(g=>new{g.Key.FromLocation,g.Key.ToLocation,count=g.Count()}).OrderByDescending(x=>x.count).Take(5).ToListAsync();

        // ── KPIs + prev-window values for real deltas ──
        var totalUsers=await _db.Users.CountAsync(u=>u.Role!="Admin");
        var totalRides=await _db.Rides.CountAsync();
        var newUsers=await _db.Users.CountAsync(u=>u.Role!="Admin"&&u.CreatedAt>=from);
        var newUsersPrev=await _db.Users.CountAsync(u=>u.Role!="Admin"&&u.CreatedAt>=prevFrom&&u.CreatedAt<from);
        var ridesWindow=await _db.Rides.CountAsync(r=>r.CreatedAt>=from);
        var ridesPrev=await _db.Rides.CountAsync(r=>r.CreatedAt>=prevFrom&&r.CreatedAt<from);
        var totalBookings=await _db.Bookings.CountAsync();
        var successRate=totalBookings>0?await _db.Bookings.CountAsync(b=>b.Status=="Completed")*100.0/totalBookings:0;

        // ── Booking funnel (window) ──
        var funnel=await _db.Bookings.Where(b=>b.CreatedAt>=from).GroupBy(b=>b.Status).Select(g=>new{status=g.Key,count=g.Count()}).ToListAsync();

        // ── Seat utilization (window, non-cancelled) ──
        var utilRows=await _db.Rides.Where(r=>r.CreatedAt>=from&&r.Status!="Cancelled"&&r.TotalSeats>0).Select(r=>new{filled=r.TotalSeats-r.AvailableSeats,r.TotalSeats}).ToListAsync();
        var utilization=utilRows.Sum(x=>x.TotalSeats)>0?Math.Round(utilRows.Sum(x=>x.filled)*100.0/utilRows.Sum(x=>x.TotalSeats),1):0;
        var utilPrevRows=await _db.Rides.Where(r=>r.CreatedAt>=prevFrom&&r.CreatedAt<from&&r.Status!="Cancelled"&&r.TotalSeats>0).Select(r=>new{filled=r.TotalSeats-r.AvailableSeats,r.TotalSeats}).ToListAsync();
        var utilizationPrev=utilPrevRows.Sum(x=>x.TotalSeats)>0?Math.Round(utilPrevRows.Sum(x=>x.filled)*100.0/utilPrevRows.Sum(x=>x.TotalSeats),1):0;

        // Unmet demand: open ride request hotspots
        var unmetDemand=await _db.RideRequests.Where(r=>r.CreatedAt>=from&&r.Status=="Open").GroupBy(r=>new{r.FromLocation,r.ToLocation}).Select(g=>new{g.Key.FromLocation,g.Key.ToLocation,waiting=g.Count()}).OrderByDescending(x=>x.waiting).Take(5).ToListAsync();
        var openRequests=await _db.RideRequests.CountAsync(r=>r.Status=="Open"&&r.LatestTime>=now);
        var fulfilledRequests=await _db.RideRequests.CountAsync(r=>r.Status=="Fulfilled"&&r.CreatedAt>=from);
        var requestsWindow=await _db.RideRequests.CountAsync(r=>r.CreatedAt>=from);

        // ── Platform volume ($, completed bookings) ──
        var volume=await _db.Bookings.Where(b=>b.Status=="Completed"&&b.CreatedAt>=from).SumAsync(b=>(decimal?)(b.SeatsBooked*b.Ride.PricePerSeat))??0;
        var volumePrev=await _db.Bookings.Where(b=>b.Status=="Completed"&&b.CreatedAt>=prevFrom&&b.CreatedAt<from).SumAsync(b=>(decimal?)(b.SeatsBooked*b.Ride.PricePerSeat))??0;

        // ── Supply vs demand by DAY-OF-WEEK × HOUR + gap recommendation ──
        var supplyTimes=await _db.Rides.Where(r=>r.CreatedAt>=from).Select(r=>r.DepartureTime).ToListAsync();
        var demandTimes=await _db.RideRequests.Where(r=>r.CreatedAt>=from).Select(r=>r.DesiredTime).ToListAsync();
        var demandAll=demandTimes.ToList();
        var slots=supplyTimes.Select(t=>new{dow=(int)t.DayOfWeek,hour=t.Hour,kind=0})
            .Concat(demandAll.Select(t=>new{dow=(int)t.DayOfWeek,hour=t.Hour,kind=1}))
            .GroupBy(x=>new{x.dow,x.hour})
            .Select(g=>new{g.Key.dow,g.Key.hour,supply=g.Count(x=>x.kind==0),demand=g.Count(x=>x.kind==1)})
            .Select(x=>new{x.dow,x.hour,x.supply,x.demand,gap=x.demand-x.supply})
            .OrderBy(x=>x.dow).ThenBy(x=>x.hour).ToList();
        var heat=supplyTimes.GroupBy(t=>new{dow=(int)t.DayOfWeek,hour=t.Hour}).Select(g=>new{g.Key.dow,g.Key.hour,count=g.Count()}).ToList();

        // ── Ride status breakdown (incl. Expired — same rule as Search) ──
        var statusRows=await _db.Rides.Select(r=>new{r.Status,r.IsRecurring,r.DepartureTime,r.RecurringEndDate}).ToListAsync();
        var statusBreakdown=statusRows.GroupBy(r=>
            r.Status=="Upcoming"&&((!r.IsRecurring&&r.DepartureTime<now)||(r.IsRecurring&&r.RecurringEndDate!=null&&r.RecurringEndDate<now))
            ?"Expired":r.Status).Select(g=>new{status=g.Key,count=g.Count()}).ToList();

        // ── Top drivers (window, completed) ──
        var topDrivers=await _db.Rides.Where(r=>r.Status=="Completed"&&r.CreatedAt>=from)
            .GroupBy(r=>new{r.DriverId,r.Driver.FullName,r.Driver.AverageRating})
            .Select(g=>new{g.Key.FullName,g.Key.AverageRating,rides=g.Count(),seats=g.Sum(r=>r.TotalSeats-r.AvailableSeats)})
            .OrderByDescending(x=>x.rides).Take(5).ToListAsync();

        // ── Ratings histogram ──
        var ratings=await _db.Ratings.Where(r=>r.CreatedAt>=from).GroupBy(r=>r.Stars).Select(g=>new{stars=g.Key,count=g.Count()}).ToListAsync();
        var avgRating=await _db.Ratings.Where(r=>r.CreatedAt>=from).Select(r=>(double?)r.Stars).AverageAsync()??0;

        // ── Safety panel ──
        var reportsOpen=await _db.Reports.CountAsync(r=>r.Status=="Open");
        var reportsResolved=await _db.Reports.CountAsync(r=>r.Status!="Open");
        var reportsByType=await _db.Reports.GroupBy(r=>r.Type).Select(g=>new{type=g.Key,count=g.Count()}).OrderByDescending(x=>x.count).Take(5).ToListAsync();
        var resolvedHours=await _db.Reports.Where(r=>r.ResolvedAt!=null).Select(r=>EF.Functions.DateDiffHour(r.CreatedAt,r.ResolvedAt!.Value)).ToListAsync();
        var avgResolutionHours=resolvedHours.Count>0?Math.Round(resolvedHours.Average(),1):0;

        // ── Geo pickup points (window) ──
        var geoPoints=await _db.Rides.Where(r=>r.CreatedAt>=from&&r.FromLat!=null&&r.FromLng!=null).Select(r=>new{lat=r.FromLat,lng=r.FromLng}).ToListAsync();

        // ── CO2 / shared km (all-time, rides with real OSRM distance) ──
        var kmRows=await _db.Rides.Where(r=>r.Status=="Completed"&&r.DistanceKm!=null).Select(r=>new{km=r.DistanceKm!.Value,pax=r.TotalSeats-r.AvailableSeats}).ToListAsync();
        var sharedKm=Math.Round(kmRows.Sum(x=>x.km*x.pax),1);
        var co2Kg=Math.Round((double)sharedKm*0.15,1);   // ~0.15 kg CO2 per avoided passenger-km

        return Ok(new{
            days,ridesPerDay,topRoutes,
            totalUsers,totalRides,newUsers,newUsersPrev,ridesWindow,ridesPrev,successRate=Math.Round(successRate,1),
            funnel,utilization,utilizationPrev,
            unmetDemand,openRequests,fulfilledRequests,requestsWindow,
            volume,volumePrev,
            slots,heat,statusBreakdown,topDrivers,
            ratings,avgRating=Math.Round(avgRating,2),
            reports=new{open=reportsOpen,resolved=reportsResolved,byType=reportsByType,avgResolutionHours},
            geoPoints,sharedKm,co2Kg
        });
    }
}
