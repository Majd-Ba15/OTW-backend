using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OTW.Api.Data;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

// ════════════════════════════════════════════
// AUTH CONTROLLER
// ════════════════════════════════════════════
[ApiController][Route("api/auth")]
public class AuthController : ControllerBase {
    readonly IAuthService  _auth;
    readonly AppSettings   _cfg;
    public AuthController(IAuthService auth, IOptions<AppSettings> cfg) { _auth=auth; _cfg=cfg.Value; }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req) {
        if (_cfg.RequireUniversityEmail) {
            var domain = req.Email.Split('@').LastOrDefault() ?? "";
            if (!_cfg.AllowedEmailDomains.Contains(domain))
                return BadRequest(new { message="University email required" });
        }
        var r = await _auth.RegisterAsync(req);
        return r.Success ? Ok(new{message=r.Message,userId=r.UserId}) : BadRequest(new{message=r.Message});
    }
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpRequest req) {
        var r = await _auth.VerifyOtpAsync(req.UserId,req.OtpCode);
        return r.Success ? Ok(new{message="Email verified"}) : BadRequest(new{message=r.Message});
    }
    [HttpPost("resend-otp")]
    public async Task<IActionResult> ResendOtp([FromBody] OtpRequest req) {
        var r = await _auth.ResendOtpAsync(req.UserId);
        return r.Success ? Ok() : BadRequest(new{message=r.Message});
    }
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req) {
        var r = await _auth.LoginAsync(req.Email,req.Password);
        return r.Success ? Ok(new{token=r.Token,userId=r.UserId,role=r.Role,name=r.FullName}) : Unauthorized(new{message=r.Message});
    }
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req) {
        await _auth.ForgotPasswordAsync(req.Email);
        return Ok(new{message="If this email exists a reset link has been sent"});
    }
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromQuery] string token,[FromBody] ResetPasswordRequest req) {
        var r = await _auth.ResetPasswordAsync(token,req.Password);
        return r.Success ? Ok(new{message="Password reset"}) : BadRequest(new{message=r.Message});
    }
}

// ════════════════════════════════════════════
// USERS CONTROLLER
// ════════════════════════════════════════════
[ApiController][Route("api/users")][Authorize]
public class UsersController : ControllerBase {
    readonly AppDbContext  _db;
    readonly IFileService  _file;
    readonly INotificationService _notif;
    readonly IEmailService _email;
    readonly AppSettings   _cfg;
    public UsersController(AppDbContext db, IFileService file, INotificationService notif, IEmailService email, IOptions<AppSettings> cfg) { _db=db; _file=file; _notif=notif; _email=email; _cfg=cfg.Value; }
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    [HttpGet("me")]
    public async Task<IActionResult> GetMe() => Ok(await _db.Users.FindAsync(Me));

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] User u) {
        var user = await _db.Users.FindAsync(Me); if(user==null) return NotFound();
        user.FullName=u.FullName; user.Phone=u.Phone; user.Faculty=u.Faculty;
        user.Gender=u.Gender; user.EmergencyContact=u.EmergencyContact; user.EmergencyPhone=u.EmergencyPhone;
        user.StudentId=u.StudentId;
        RecalcCompletion(user);
        await _db.SaveChangesAsync(); return Ok(new{message="Profile updated"});
    }

    [HttpPost("photo")]
    public async Task<IActionResult> UploadPhoto(IFormFile file) {
        var url=await _file.SaveAsync(file,"profiles");
        var user=await _db.Users.FindAsync(Me); if(user==null) return NotFound();
        user.ProfilePhoto=url; RecalcCompletion(user);
        await _db.SaveChangesAsync(); return Ok(new{url});
    }

    [HttpPost("upload-id")]
    public async Task<IActionResult> UploadId(IFormFile file) {
        var url=await _file.SaveAsync(file,"student-ids");
        var user=await _db.Users.FindAsync(Me); if(user==null) return NotFound();
        user.StudentIdPhoto=url;
        if(_cfg.AutoApproveVerification) { user.IsVerified=true; }
        RecalcCompletion(user);
        await _db.SaveChangesAsync();
        return Ok(new{message=_cfg.AutoApproveVerification?"Auto-verified":"ID uploaded. Awaiting admin approval.",isVerified=user.IsVerified,url});
    }

    [HttpPost("car")]
    public async Task<IActionResult> SaveCar([FromBody] Car c) {
        var existing=await _db.Cars.FirstOrDefaultAsync(x=>x.DriverId==Me);
        if(existing!=null) { existing.Model=c.Model; existing.Colour=c.Colour; existing.PlateNumber=c.PlateNumber; existing.TotalSeats=c.TotalSeats; }
        else { c.DriverId=Me; _db.Cars.Add(c); }
        var user=await _db.Users.FindAsync(Me); if(user!=null) { RecalcCompletion(user); }
        await _db.SaveChangesAsync(); return Ok(new{message="Car saved"});
    }

    [HttpPost("car/photo")]
    public async Task<IActionResult> UploadCarPhoto(IFormFile file,[FromQuery] string type) {
        var url=await _file.SaveAsync(file,"cars");
        var car=await _db.Cars.FirstOrDefaultAsync(c=>c.DriverId==Me); if(car==null) return NotFound();
        if(type=="front") car.CarPhotoFront=url;
        if(type=="side")  car.CarPhotoSide=url;
        if(type=="licence") car.LicencePhoto=url;
        await _db.SaveChangesAsync(); return Ok(new{url});
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats() {
        var completed = await _db.Bookings.CountAsync(b=>b.RiderId==Me&&b.Status=="Completed");
        var rating    = await _db.Users.Where(u=>u.UserId==Me).Select(u=>u.AverageRating).FirstOrDefaultAsync();
        return Ok(new{completedRides=completed,averageRating=rating});
    }

    [HttpGet("{id}/profile")]
    public async Task<IActionResult> GetProfile(int id) {
        var u=await _db.Users.Where(x=>x.UserId==id).Select(x=>new{x.UserId,x.FullName,x.Faculty,x.Gender,x.ProfilePhoto,x.AverageRating,x.IsVerified,x.Role}).FirstOrDefaultAsync();
        return u==null?NotFound():Ok(u);
    }

    [HttpGet("favourites")]
    public async Task<IActionResult> GetFavourites() =>
        Ok(await _db.FavouriteRoutes.Where(f=>f.UserId==Me).OrderByDescending(f=>f.UseCount).ToListAsync());

    [HttpPost("favourites")]
    public async Task<IActionResult> AddFavourite([FromBody] FavouriteRoute f) {
        f.UserId=Me; _db.FavouriteRoutes.Add(f); await _db.SaveChangesAsync(); return Ok();
    }

    [HttpDelete("favourites/{id}")]
    public async Task<IActionResult> DeleteFavourite(int id) {
        var f=await _db.FavouriteRoutes.FirstOrDefaultAsync(x=>x.FavouriteId==id&&x.UserId==Me);
        if(f==null) return NotFound(); _db.FavouriteRoutes.Remove(f); await _db.SaveChangesAsync(); return Ok();
    }

    [HttpPut("availability")]
    public async Task<IActionResult> ToggleAvailability() {
        var u=await _db.Users.FindAsync(Me); if(u==null) return NotFound();
        u.IsAvailable=!u.IsAvailable; await _db.SaveChangesAsync();
        return Ok(new{isAvailable=u.IsAvailable});
    }

    [HttpPost("remind-verification")]
    public async Task<IActionResult> RemindVerification() {
        var u=await _db.Users.FindAsync(Me); if(u==null) return NotFound();
        if(u.IsVerified) return BadRequest(new{message="Already verified"});
        await _email.SendAdminVerificationReminderAsync(_cfg.AdminEmail, u.FullName, u.Email, u.UserId);
        return Ok(new{message="Reminder sent to admin"});
    }

    void RecalcCompletion(User u) {
        int score=0;
        if(!string.IsNullOrEmpty(u.FullName))         score+=10;
        if(!string.IsNullOrEmpty(u.Email))            score+=10;
        if(!string.IsNullOrEmpty(u.Phone))            score+=15;
        if(!string.IsNullOrEmpty(u.StudentId))        score+=15;
        if(!string.IsNullOrEmpty(u.Faculty))          score+=10;
        if(!string.IsNullOrEmpty(u.ProfilePhoto))     score+=15;
        if(!string.IsNullOrEmpty(u.StudentIdPhoto))   score+=15;
        if(!string.IsNullOrEmpty(u.EmergencyContact)) score+=10;
        u.ProfileCompletion=score;
    }
}

// ════════════════════════════════════════════
// RIDES CONTROLLER
// ════════════════════════════════════════════
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
        var q = _db.Rides.Include(r=>r.Driver).Include(r=>r.Car).Where(r=>r.Status=="Upcoming"&&r.AvailableSeats>=seats);
        if(!string.IsNullOrEmpty(from)) q=q.Where(r=>r.FromLocation.Contains(from));
        if(!string.IsNullOrEmpty(to))   q=q.Where(r=>r.ToLocation.Contains(to));
        if(date.HasValue)               q=q.Where(r=>r.DepartureTime.Date==date.Value.Date);
        if(!string.IsNullOrEmpty(gender)&&gender!="Any") q=q.Where(r=>r.GenderPreference=="Any"||r.GenderPreference==gender);
        q=sort=="price"?q.OrderBy(r=>r.PricePerSeat):q.OrderBy(r=>r.DepartureTime);
        var rides=await q.Select(r=>new{r.RideId,r.FromLocation,r.ToLocation,r.DepartureTime,r.AvailableSeats,r.TotalSeats,r.PricePerSeat,r.GenderPreference,r.Notes,r.ShareToken,
            Driver=new{r.Driver.FullName,r.Driver.AverageRating,r.Driver.ProfilePhoto,r.Driver.IsVerified},
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
        var q=_db.Rides.Include(r=>r.Bookings).Where(r=>r.DriverId==Me);
        if(!string.IsNullOrEmpty(status)) q=q.Where(r=>r.Status==status);
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
        var ride=new Ride{DriverId=Me,CarId=car?.CarId,FromLocation=req.FromLocation,ToLocation=req.ToLocation,FromLat=req.FromLat,FromLng=req.FromLng,ToLat=req.ToLat,ToLng=req.ToLng,DepartureTime=req.DepartureTime,TotalSeats=req.TotalSeats,AvailableSeats=req.TotalSeats,PricePerSeat=req.PricePerSeat,GenderPreference=req.GenderPreference,Notes=req.Notes,IsRecurring=req.IsRecurring,RecurringDays=req.RecurringDays,RecurringEndDate=req.RecurringEndDate,ShareToken=Guid.NewGuid().ToString("N")[..8],Status="Upcoming"};
        _db.Rides.Add(ride); await _db.SaveChangesAsync();
        foreach(var s in req.Stops) _db.RideStops.Add(new RideStop{RideId=ride.RideId,StopName=s.StopName,StopLat=s.StopLat,StopLng=s.StopLng,StopPrice=s.StopPrice,StopOrder=s.StopOrder});
        if(req.Stops.Count>0) await _db.SaveChangesAsync();
        return Ok(new{message="Ride posted",rideId=ride.RideId,shareToken=ride.ShareToken});
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateRide(int id,[FromBody] PostRideRequest req) {
        var r=await _db.Rides.FindAsync(id); if(r==null||r.DriverId!=Me) return Forbid();
        r.FromLocation=req.FromLocation; r.ToLocation=req.ToLocation; r.DepartureTime=req.DepartureTime;
        r.PricePerSeat=req.PricePerSeat; r.GenderPreference=req.GenderPreference; r.Notes=req.Notes;
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
        Ok(await _db.Bookings.Include(b=>b.Rider).Where(b=>b.RideId==id&&b.Status=="Confirmed").Select(b=>new{b.BookingId,Rider=new{b.Rider.UserId,b.Rider.FullName,b.Rider.StudentId,b.Rider.Faculty,b.Rider.ProfilePhoto,b.Rider.AverageRating}}).ToListAsync());

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
                "Ride completed — please rate your driver ⭐",
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

// ════════════════════════════════════════════
// BOOKINGS CONTROLLER
// ════════════════════════════════════════════
[ApiController][Route("api/bookings")][Authorize]
public class BookingsController : ControllerBase {
    readonly AppDbContext         _db;
    readonly INotificationService _notif;
    readonly IEmailService        _email;
    public BookingsController(AppDbContext db, INotificationService notif, IEmailService email) {
        _db=db; _notif=notif; _email=email;
    }
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    [HttpPost]
    public async Task<IActionResult> Book([FromBody] BookingRequest req) {
        await using var tx=await _db.Database.BeginTransactionAsync();
        try {
            var ride=await _db.Rides.FromSqlRaw("SELECT * FROM Rides WITH (UPDLOCK) WHERE RideId={0}",req.RideId).FirstOrDefaultAsync();
            if(ride==null) return NotFound(new{message="Ride not found"});
            if(ride.AvailableSeats<req.SeatsBooked) {
                int pos=await _db.Waitlist.Where(w=>w.RideId==req.RideId).CountAsync()+1;
                _db.Waitlist.Add(new Waitlist{RideId=req.RideId,RiderId=Me,Position=pos});
                await _db.SaveChangesAsync(); await tx.CommitAsync();
                return Ok(new{message="Ride full — added to waitlist",waitlistPosition=pos});
            }
            var exists=await _db.Bookings.AnyAsync(b=>b.RideId==req.RideId&&b.RiderId==Me&&(b.Status=="Pending"||b.Status=="Confirmed"));
            if(exists) return BadRequest(new{message="Already booked"});
            var booking=new Booking{RideId=req.RideId,RiderId=Me,SeatsBooked=req.SeatsBooked,Note=req.Note,Status="Pending"};
            _db.Bookings.Add(booking); await _db.SaveChangesAsync(); await tx.CommitAsync();
            await _notif.CreateAsync(ride.DriverId,"New booking request",$"A rider wants to join your ride","BookingRequest",booking.BookingId);
            return Ok(new{message="Booking request sent",bookingId=booking.BookingId});
        } catch { await tx.RollbackAsync(); return StatusCode(500,new{message="Booking failed, try again"}); }
    }

    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcoming() =>
        Ok(await _db.Bookings.Include(b=>b.Ride).ThenInclude(r=>r.Driver).Where(b=>b.RiderId==Me&&(b.Status=="Confirmed"||b.Status=="Pending")&&b.Ride.DepartureTime>DateTime.UtcNow).OrderBy(b=>b.Ride.DepartureTime).ToListAsync());

    [HttpGet("active")]
    public async Task<IActionResult> GetActive() =>
        Ok(await _db.Bookings.Include(b=>b.Ride).ThenInclude(r=>r.Driver).Include(b=>b.Ride).ThenInclude(r=>r.Car).FirstOrDefaultAsync(b=>b.RiderId==Me&&b.Status=="Confirmed"&&b.Ride.Status=="Active"));

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery]string? status) {
        var q=_db.Bookings.Include(b=>b.Ride).ThenInclude(r=>r.Driver).Where(b=>b.RiderId==Me);
        if(!string.IsNullOrEmpty(status)) q=q.Where(b=>b.Status==status);
        return Ok(await q.OrderByDescending(b=>b.CreatedAt).ToListAsync());
    }

    [HttpGet("requests")]
    public async Task<IActionResult> GetRequests() =>
        Ok(await _db.Bookings.Include(b=>b.Rider).Include(b=>b.Ride).Where(b=>b.Ride.DriverId==Me&&b.Status=="Pending").OrderByDescending(b=>b.CreatedAt).ToListAsync());

    [HttpPut("{id}/accept")]
    public async Task<IActionResult> Accept(int id) {
        var b=await _db.Bookings
            .Include(x=>x.Ride).ThenInclude(r=>r.Car)
            .Include(x=>x.Ride).ThenInclude(r=>r.Driver)
            .Include(x=>x.Rider)
            .FirstOrDefaultAsync(x=>x.BookingId==id);
        if(b==null||b.Ride.DriverId!=Me) return Forbid();
        b.Status="Confirmed"; b.UpdatedAt=DateTime.UtcNow;
        b.Ride.AvailableSeats-=b.SeatsBooked;
        await _db.SaveChangesAsync();
        // In-app notification
        await _notif.CreateAsync(b.RiderId,
            "Booking confirmed! ✓",
            $"Your seat on {b.Ride.FromLocation} → {b.Ride.ToLocation} is confirmed",
            "Confirmed", b.BookingId);
        // Email to rider
        await _email.SendBookingAcceptedAsync(
            b.Rider.Email,
            b.Rider.FullName,
            b.Ride.FromLocation,
            b.Ride.ToLocation,
            b.Ride.DepartureTime.ToString("dddd dd MMM yyyy, h:mm tt"),
            b.Ride.Driver.FullName,
            b.Ride.Car?.PlateNumber ?? "—");
        return Ok(new{message="Accepted"});
    }

    [HttpPut("{id}/decline")]
    public async Task<IActionResult> Decline(int id) {
        var b=await _db.Bookings
            .Include(x=>x.Ride)
            .Include(x=>x.Rider)
            .FirstOrDefaultAsync(x=>x.BookingId==id);
        if(b==null||b.Ride.DriverId!=Me) return Forbid();
        b.Status="Declined"; b.UpdatedAt=DateTime.UtcNow;
        await _db.SaveChangesAsync();
        // In-app notification
        await _notif.CreateAsync(b.RiderId,
            "Booking not accepted",
            "Your booking request was declined. Search for another ride.",
            "Declined", b.BookingId);
        // Email to rider
        await _email.SendBookingDeclinedAsync(
            b.Rider.Email,
            b.Rider.FullName,
            b.Ride.FromLocation,
            b.Ride.ToLocation,
            b.Ride.DepartureTime.ToString("dddd dd MMM yyyy, h:mm tt"));
        return Ok(new{message="Declined"});
    }

    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> Cancel(int id) {
        var b=await _db.Bookings
            .Include(x=>x.Ride)
            .FirstOrDefaultAsync(x=>x.BookingId==id&&x.RiderId==Me);
        if(b==null) return NotFound();
        var wasConfirmed = b.Status=="Confirmed";
        if(wasConfirmed) b.Ride.AvailableSeats+=b.SeatsBooked;
        b.Status="Cancelled"; b.UpdatedAt=DateTime.UtcNow;
        await _db.SaveChangesAsync();
        // Promote first person on waitlist if seat opened
        if(wasConfirmed) {
            var next=await _db.Waitlist
                .Include(w=>w.Rider)
                .Where(w=>w.RideId==b.RideId)
                .OrderBy(w=>w.Position)
                .FirstOrDefaultAsync();
            if(next!=null) {
                await _notif.CreateAsync(next.RiderId,
                    "A seat opened for you! 🎉",
                    $"A seat is now available on {b.Ride.FromLocation} → {b.Ride.ToLocation}",
                    "Waitlist", b.RideId);
                await _email.SendWaitlistPromotedAsync(
                    next.Rider.Email,
                    next.Rider.FullName,
                    b.Ride.FromLocation,
                    b.Ride.ToLocation,
                    b.Ride.DepartureTime.ToString("dddd dd MMM yyyy, h:mm tt"),
                    b.RideId);
            }
        }
        return Ok(new{message="Cancelled"});
    }

    [HttpPut("{id}/complete")]
    public async Task<IActionResult> Complete(int id) {
        var b=await _db.Bookings
            .Include(x=>x.Ride).ThenInclude(r=>r.Driver)
            .Include(x=>x.Rider)
            .FirstOrDefaultAsync(x=>x.BookingId==id);
        if(b==null) return NotFound();
        b.Status="Completed"; b.UpdatedAt=DateTime.UtcNow;
        await _db.SaveChangesAsync();
        // Email rider to rate driver
        await _email.SendRateRideAsync(
            b.Rider.Email, b.Rider.FullName,
            "Rider", b.Ride.Driver.FullName, b.BookingId);
        // Email driver to rate rider
        await _email.SendRateRideAsync(
            b.Ride.Driver.Email, b.Ride.Driver.FullName,
            "Driver", b.Rider.FullName, b.BookingId);
        return Ok();
    }
}
