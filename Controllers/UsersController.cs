using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OTW.Api.Data;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

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
        // Partial update — callers send only the fields they change (e.g. {phone}
        // from the profile page). Blind assignment used to blank everything else.
        if(!string.IsNullOrEmpty(u.FullName)) user.FullName=u.FullName;
        if(u.Phone!=null)            user.Phone=u.Phone;
        if(u.Faculty!=null)          user.Faculty=u.Faculty;
        if(u.Gender!=null)           user.Gender=u.Gender;
        if(u.EmergencyContact!=null) user.EmergencyContact=u.EmergencyContact;
        if(u.EmergencyPhone!=null)   user.EmergencyPhone=u.EmergencyPhone;
        if(u.StudentId!=null)        user.StudentId=u.StudentId;
        // CampusName only — University itself is set once at registration and
        // is not editable here (changing it would bypass the domain check).
        if(u.CampusName!=null)       user.CampusName=u.CampusName;
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
    public async Task<IActionResult> UploadId([FromBody] UploadRequest req) {
        var user=await _db.Users.FindAsync(Me); if(user==null) return NotFound();
        if(string.IsNullOrEmpty(req.base64)) return BadRequest(new{message="No file data provided"});

        try {
            // Convert base64 to file bytes
            var fileBytes = Convert.FromBase64String(req.base64);
            var fileName = req.name ?? "student-id.jpg";
            var stream = new MemoryStream(fileBytes);
            var formFile = new FormFile(stream, 0, fileBytes.Length, "file", fileName);

            // Save file
            var url = await _file.SaveAsync(formFile, "student-ids");
            if(string.IsNullOrEmpty(url)) return BadRequest(new{message="File save failed"});

            // Update user record
            user.StudentIdPhoto=url;
            if(_cfg.AutoApproveVerification) { user.IsVerified=true; }
            RecalcCompletion(user);
            await _db.SaveChangesAsync();

            return Ok(new{message=_cfg.AutoApproveVerification?"Auto-verified":"ID uploaded. Awaiting admin approval.",isVerified=user.IsVerified,url});
        } catch(Exception ex) {
            return BadRequest(new{message="Upload failed",error=ex.Message});
        }
    }

    [HttpGet("car")]
    public async Task<IActionResult> GetCar() =>
        Ok(await _db.Cars.FirstOrDefaultAsync(c=>c.DriverId==Me));

    [HttpPost("car")]
    public async Task<IActionResult> SaveCar([FromBody] CarRequest c) {
        var existing=await _db.Cars.FirstOrDefaultAsync(x=>x.DriverId==Me);
        if(existing!=null) { existing.Model=c.Model; existing.Colour=c.Colour; existing.PlateNumber=c.PlateNumber; existing.TotalSeats=c.TotalSeats; }
        else { _db.Cars.Add(new Car{DriverId=Me,Model=c.Model,Colour=c.Colour,PlateNumber=c.PlateNumber,TotalSeats=c.TotalSeats}); }
        var user=await _db.Users.FindAsync(Me); if(user!=null) { RecalcCompletion(user); }
        await _db.SaveChangesAsync(); return Ok(new{message="Car saved"});
    }

    [HttpPost("submit-verification")]
    public async Task<IActionResult> SubmitVerification() {
        var user=await _db.Users.FindAsync(Me);
        if(user==null) return NotFound();
        if(user.Role!="Driver") return BadRequest(new{message="Only drivers can submit verification"});

        // Mark driver as ready for verification
        // Set StudentIdPhoto to placeholder if not already set (ensures it appears in pending list)
        if(string.IsNullOrEmpty(user.StudentIdPhoto)) {
            user.StudentIdPhoto="/uploads/placeholder.jpg";
        }
        user.IsVerified=false; // Ensure not auto-verified
        user.IsActive=true;    // Ensure account is active

        await _db.SaveChangesAsync();

        return Ok(new{message="Verification submitted. Admin will review your documents.",userId=user.UserId});
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
        // Driver earnings — real money from Confirmed + Completed bookings on my
        // rides (cash: seats × price per seat). Zero for riders, harmless.
        var mine      = _db.Bookings.Where(b=>b.Ride.DriverId==Me&&(b.Status=="Confirmed"||b.Status=="Completed"));
        var earnings  = await mine.SumAsync(b=>(decimal?)(b.SeatsBooked*b.Ride.PricePerSeat))??0;
        var weekAgo   = DateTime.UtcNow.AddDays(-7);
        var weekEarnings = await mine.Where(b=>b.CreatedAt>=weekAgo).SumAsync(b=>(decimal?)(b.SeatsBooked*b.Ride.PricePerSeat))??0;
        var totalRides = await mine.Select(b=>b.RideId).Distinct().CountAsync();
        var thisWeek = await mine.Where(b=>(b.UpdatedAt??b.CreatedAt)>=weekAgo).Select(b=>b.RideId).Distinct().CountAsync();
        var transactions = await mine.OrderByDescending(b=>b.UpdatedAt??b.CreatedAt).Take(10)
            .Select(b=>new{b.BookingId,label=b.Ride.FromLocation+" → "+b.Ride.ToLocation,passenger=b.Rider.FullName,
                amount=b.SeatsBooked*b.Ride.PricePerSeat,seats=b.SeatsBooked,time=b.UpdatedAt??b.CreatedAt,b.Status}).ToListAsync();
        return Ok(new{completedRides=completed,averageRating=rating,earnings,weekEarnings,totalRides,thisWeek,driverBookedRides=totalRides,transactions});
    }

    [HttpGet("{id}/profile")]
    public async Task<IActionResult> GetProfile(int id) {
        var u=await _db.Users.Where(x=>x.UserId==id).Select(x=>new{x.UserId,x.FullName,x.Faculty,x.Gender,x.ProfilePhoto,x.AverageRating,x.IsVerified,x.Role,x.University,x.CampusName}).FirstOrDefaultAsync();
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
