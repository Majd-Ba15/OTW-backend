using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OTW.Api.Data;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

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
            if(ride.AvailableSeats<req.SeatsBooked) return BadRequest(new{message="Ride is full"});
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
            "Booking confirmed! âœ“",
            $"Your seat on {b.Ride.FromLocation} â†’ {b.Ride.ToLocation} is confirmed",
            "Confirmed", b.BookingId);
        // Email to rider
        await _email.SendBookingAcceptedAsync(
            b.Rider.Email,
            b.Rider.FullName,
            b.Ride.FromLocation,
            b.Ride.ToLocation,
            b.Ride.DepartureTime.ToString("dddd dd MMM yyyy, h:mm tt"),
            b.Ride.Driver.FullName,
            b.Ride.Car?.PlateNumber ?? "â€”");
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
