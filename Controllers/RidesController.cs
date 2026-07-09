using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OTW.Api.Data;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

[ApiController][Route("api/rides")][Authorize]
public class RidesController : ControllerBase {
    readonly AppDbContext         _db;
    readonly IEmailService        _email;
    readonly INotificationService _notif;
    public RidesController(AppDbContext db, IEmailService email, INotificationService notif) {
        _db=db; _email=email; _notif=notif;
    }
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery]string? from,[FromQuery]string? to,[FromQuery]DateTime? date,[FromQuery]int seats=1,[FromQuery]string? gender=null,[FromQuery]string sort="time") {
        // Active rides only: Upcoming with seats, and not expired. A one-time ride
        // expires once its departure passes; a recurring ride expires only once
        // its RecurringEndDate passes (open-ended recurring rides never expire).
        var now = DateTime.UtcNow;
        var q = _db.Rides.Include(r=>r.Driver).Include(r=>r.Car).Where(r=>r.Status=="Upcoming"&&r.AvailableSeats>=seats
            &&((!r.IsRecurring&&r.DepartureTime>=now)||(r.IsRecurring&&(r.RecurringEndDate==null||r.RecurringEndDate>=now))));
        if(!string.IsNullOrEmpty(from)) q=q.Where(r=>r.FromLocation.Contains(from));
        if(!string.IsNullOrEmpty(to))   q=q.Where(r=>r.ToLocation.Contains(to));
        if(date.HasValue)               q=q.Where(r=>r.DepartureTime.Date==date.Value.Date);
        if(!string.IsNullOrEmpty(gender)&&gender!="Any") q=q.Where(r=>r.GenderPreference=="Any"||r.GenderPreference==gender);
        q=sort=="price"?q.OrderBy(r=>r.PricePerSeat):q.OrderBy(r=>r.DepartureTime);
        var rides=await q.Select(r=>new{r.RideId,r.FromLocation,r.ToLocation,r.FromLat,r.FromLng,r.ToLat,r.ToLng,r.DepartureTime,r.AvailableSeats,r.TotalSeats,r.PricePerSeat,r.GenderPreference,r.Notes,r.ShareToken,
            Stops=r.Stops.OrderBy(s=>s.StopOrder).Select(s=>s.StopName).ToList(),
            Driver=new{r.Driver.FullName,r.Driver.AverageRating,r.Driver.ProfilePhoto,r.Driver.IsVerified,r.Driver.University,r.Driver.CampusName},
            Car=r.Car==null?null:new{r.Car.Model,r.Car.Colour,r.Car.PlateNumber}}).ToListAsync();
        return Ok(rides);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id) {
        var r=await _db.Rides.Include(x=>x.Driver).Include(x=>x.Car).Include(x=>x.Stops).FirstOrDefaultAsync(x=>x.RideId==id);
        return r==null?NotFound():Ok(r);
    }

    [HttpGet("share/{token}")][AllowAnonymous]
    public async Task<IActionResult> GetByToken(string token) {
        var r=await _db.Rides.Include(x=>x.Driver).Include(x=>x.Car).FirstOrDefaultAsync(x=>x.ShareToken==token);
        return r==null?NotFound():Ok(r);
    }

    [HttpGet("mine")]
    public async Task<IActionResult> GetMine([FromQuery]string? status) {
        var q=_db.Rides.Include(r=>r.Bookings).Include(r=>r.Stops).Where(r=>r.DriverId==Me);
        if(!string.IsNullOrEmpty(status)) q=status.Equals("recurring", StringComparison.OrdinalIgnoreCase) ? q.Where(r=>r.IsRecurring) : q.Where(r=>r.Status==status);
        return Ok(await q.OrderByDescending(r=>r.DepartureTime).ToListAsync());
    }

    [HttpGet("mine/active")]
    public async Task<IActionResult> GetActiveRide() {
        var r=await _db.Rides.Include(x=>x.Bookings).ThenInclude(b=>b.Rider).Include(x=>x.Car).FirstOrDefaultAsync(x=>x.DriverId==Me&&x.Status=="Active");
        return Ok(r);
    }

    [HttpPost]
    public async Task<IActionResult> PostRide([FromBody] PostRideRequest req) {
        var user=await _db.Users.FindAsync(Me);
        if(user?.Role!="Driver") return Forbid();
        var car=await _db.Cars.FirstOrDefaultAsync(c=>c.DriverId==Me);
        var ride=new Ride{DriverId=Me,CarId=car?.CarId,FromLocation=req.FromLocation,ToLocation=req.ToLocation,FromLat=req.FromLat,FromLng=req.FromLng,ToLat=req.ToLat,ToLng=req.ToLng,DistanceKm=req.DistanceKm,DurationMin=req.DurationMin,DepartureTime=req.DepartureTime,TotalSeats=req.TotalSeats,AvailableSeats=req.TotalSeats,PricePerSeat=req.PricePerSeat,GenderPreference=req.GenderPreference,Notes=req.Notes,IsRecurring=req.IsRecurring,RecurringDays=req.RecurringDays,RecurringEndDate=req.RecurringEndDate,ShareToken=Guid.NewGuid().ToString("N")[..8],Status="Upcoming"};
        _db.Rides.Add(ride); await _db.SaveChangesAsync();
        foreach(var s in req.Stops) _db.RideStops.Add(new RideStop{RideId=ride.RideId,StopName=s.StopName,StopLat=s.StopLat,StopLng=s.StopLng,StopPrice=s.StopPrice,StopOrder=s.StopOrder});
        if(req.Stops.Count>0) await _db.SaveChangesAsync();
        return Ok(new{message="Ride posted",rideId=ride.RideId,shareToken=ride.ShareToken});
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateRide(int id,[FromBody] PostRideRequest req) {
        var r=await _db.Rides.Include(x=>x.Stops).FirstOrDefaultAsync(x=>x.RideId==id); if(r==null||r.DriverId!=Me) return Forbid();
        // Partial update — callers send only what they change (e.g. {status} from
        // pause/resume, {stops} from the stops editor). Blindly assigning every
        // field here used to blank locations and zero the price on those calls.
        if(!string.IsNullOrEmpty(req.FromLocation))     r.FromLocation=req.FromLocation;
        if(!string.IsNullOrEmpty(req.ToLocation))       r.ToLocation=req.ToLocation;
        if(req.DepartureTime!=default)                  r.DepartureTime=req.DepartureTime;
        if(req.PricePerSeat>0)                          r.PricePerSeat=req.PricePerSeat;
        if(!string.IsNullOrEmpty(req.GenderPreference)) r.GenderPreference=req.GenderPreference;
        if(req.Notes!=null)                             r.Notes=req.Notes;
        if(!string.IsNullOrEmpty(req.Status))           r.Status=req.Status;
        if(req.FromLat.HasValue) r.FromLat=req.FromLat; if(req.FromLng.HasValue) r.FromLng=req.FromLng;
        if(req.ToLat.HasValue)   r.ToLat=req.ToLat;     if(req.ToLng.HasValue)   r.ToLng=req.ToLng;
        // Replace the ride's stops when a new list is provided
        if(req.Stops is {Count:>0}) {
            _db.RideStops.RemoveRange(r.Stops);
            foreach(var s in req.Stops) _db.RideStops.Add(new RideStop{RideId=r.RideId,StopName=s.StopName,StopLat=s.StopLat,StopLng=s.StopLng,StopPrice=s.StopPrice,StopOrder=s.StopOrder});
        }
        await _db.SaveChangesAsync(); return Ok(new{message="Ride updated"});
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> CancelRide(int id) {
        var r=await _db.Rides.Include(x=>x.Bookings).FirstOrDefaultAsync(x=>x.RideId==id);
        if(r==null||r.DriverId!=Me) return Forbid();
        r.Status="Cancelled";
        foreach(var b in r.Bookings.Where(b=>b.Status=="Confirmed"||b.Status=="Pending")) b.Status="Cancelled";
        await _db.SaveChangesAsync(); return Ok(new{message="Ride cancelled"});
    }

    [HttpGet("{id}/passengers")]
    public async Task<IActionResult> GetPassengers(int id) =>
        Ok(await _db.Bookings.Include(b=>b.Rider).Where(b=>b.RideId==id&&b.Status=="Confirmed").Select(b=>new{b.BookingId,b.Note,b.Status,Rider=new{b.Rider.UserId,b.Rider.FullName,b.Rider.StudentId,b.Rider.Faculty,b.Rider.Phone,b.Rider.ProfilePhoto,b.Rider.AverageRating,b.Rider.University,b.Rider.CampusName}}).ToListAsync());

    [HttpPut("{id}/start")]
    public async Task<IActionResult> StartRide(int id) {
        var r=await _db.Rides.FindAsync(id);
        if(r==null||r.DriverId!=Me) return Forbid();
        r.Status="Active"; await _db.SaveChangesAsync();
        return Ok(new{message="Ride started"});
    }

    [HttpPut("{id}/end")]
    public async Task<IActionResult> EndRide(int id) {
        var r=await _db.Rides
            .Include(x=>x.Bookings).ThenInclude(b=>b.Rider)
            .Include(x=>x.Driver)
            .Include(x=>x.Car)
            .FirstOrDefaultAsync(x=>x.RideId==id);
        if(r==null||r.DriverId!=Me) return Forbid();
        r.Status="Completed";
        // Complete all confirmed bookings + send rate emails
        foreach(var b in r.Bookings.Where(b=>b.Status=="Confirmed")) {
            b.Status="Completed"; b.UpdatedAt=DateTime.UtcNow;
            // Email rider to rate driver
            await _email.SendRateRideAsync(
                b.Rider.Email, b.Rider.FullName,
                "Rider", r.Driver.FullName, b.BookingId);
            // Notification to rider
            await _notif.CreateAsync(b.RiderId,
                "Ride completed â€” please rate your driver â­",
                $"How was your ride with {r.Driver.FullName}?",
                "RateRide", b.BookingId);
        }
        // Email driver to rate riders
        foreach(var b in r.Bookings.Where(b=>b.Status=="Completed")) {
            await _email.SendRateRideAsync(
                r.Driver.Email, r.Driver.FullName,
                "Driver", b.Rider.FullName, b.BookingId);
        }
        await _db.SaveChangesAsync();
        return Ok(new{message="Ride completed"});
    }
}
