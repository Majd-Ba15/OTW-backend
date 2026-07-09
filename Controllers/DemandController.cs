using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTW.Api.Data;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

[ApiController]
[Route("api/demand")]
[Authorize]
public class DemandController : ControllerBase {
    readonly AppDbContext _db;
    readonly INotificationService _notif;
    readonly IEmailService _email;
    public DemandController(AppDbContext db, INotificationService notif, IEmailService email) {
        _db = db; _notif = notif; _email = email;
    }

    int Me => int.Parse(User.FindFirst("userId")!.Value);
    // JWT middleware remaps the "role" claim to ClaimTypes.Role by default,
    // so check both — FindFirst("role") alone always misses and returns 403.
    string Role => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                ?? User.FindFirst("role")?.Value ?? "";

    static bool RouteMatch(string a, string b) =>
        string.IsNullOrWhiteSpace(a) ||
        string.IsNullOrWhiteSpace(b) ||
        a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
        b.Contains(a, StringComparison.OrdinalIgnoreCase);

    static object RequestDto(RideRequest r) => new {
        r.RideRequestId, r.FromLocation, r.ToLocation, r.FromLat, r.FromLng, r.ToLat, r.ToLng, r.DesiredTime, r.EarliestTime, r.LatestTime,
        r.SeatsNeeded, r.MaxPrice, r.GenderPreference, r.Note, r.Status, r.MatchedDriverId,
        r.MatchedRideId, r.CreatedAt,
        Rider = r.Rider == null ? null : new { r.Rider.UserId, r.Rider.FullName, r.Rider.AverageRating, r.Rider.ProfilePhoto, r.Rider.University, r.Rider.CampusName },
        MatchedDriver = r.MatchedDriver == null ? null : new { r.MatchedDriver.UserId, r.MatchedDriver.FullName, r.MatchedDriver.AverageRating, r.MatchedDriver.ProfilePhoto, r.MatchedDriver.University, r.MatchedDriver.CampusName }
    };

    static object AvailabilityDto(DriverAvailability a) => new {
        a.DriverAvailabilityId, a.FromLocation, a.ToLocation, a.AvailableFrom, a.AvailableTo,
        a.Seats, a.SuggestedPrice, a.Note, a.IsActive, a.CreatedAt,
        Driver = a.Driver == null ? null : new { a.Driver.UserId, a.Driver.FullName, a.Driver.AverageRating, a.Driver.ProfilePhoto, a.Driver.IsVerified, a.Driver.University, a.Driver.CampusName }
    };

    [HttpPost("requests")]
    public async Task<IActionResult> CreateRequest([FromBody] RideRequestCreateRequest req) {
        if (Role != "Rider") return Forbid();
        var earliest = req.EarliestTime ?? req.DesiredTime.AddMinutes(-30);
        var latest = req.LatestTime ?? req.DesiredTime.AddMinutes(30);
        if (latest < earliest) return BadRequest(new { message = "Latest time must be after earliest time" });

        var rideRequest = new RideRequest {
            RiderId = Me,
            FromLocation = req.FromLocation,
            ToLocation = req.ToLocation,
            DesiredTime = req.DesiredTime,
            EarliestTime = earliest,
            LatestTime = latest,
            SeatsNeeded = Math.Max(1, req.SeatsNeeded),
            MaxPrice = req.MaxPrice,
            GenderPreference = req.GenderPreference,
            Note = req.Note,
            Status = "Open"
        };
        _db.RideRequests.Add(rideRequest);
        await _db.SaveChangesAsync();

        // Notify drivers whose active availability matches this request so demand
        // reaches supply immediately instead of waiting to be discovered.
        var slots = await _db.DriverAvailabilities
            .Where(a => a.IsActive && a.AvailableTo >= DateTime.UtcNow && a.Seats >= rideRequest.SeatsNeeded &&
                        a.AvailableFrom <= rideRequest.LatestTime && a.AvailableTo >= rideRequest.EarliestTime)
            .ToListAsync();
        var driverIds = slots
            .Where(a => RouteMatch(a.FromLocation, rideRequest.FromLocation) && RouteMatch(a.ToLocation, rideRequest.ToLocation))
            .Select(a => a.DriverId).Distinct().ToList();
        foreach (var driverId in driverIds)
            await _notif.CreateAsync(driverId, "New ride request matches your availability",
                $"{rideRequest.FromLocation} → {rideRequest.ToLocation}, {rideRequest.DesiredTime:ddd dd MMM h:mm tt}",
                "RideRequest", rideRequest.RideRequestId);

        // No matching availability at all → this is a supply gap. Nudge active,
        // verified drivers to post a ride at exactly this day + time.
        if (driverIds.Count == 0) {
            var fallbackDrivers = await _db.Users
                .Where(u => u.Role == "Driver" && u.IsActive && u.IsVerified && u.IsAvailable)
                .Select(u => u.UserId).Take(25).ToListAsync();
            foreach (var driverId in fallbackDrivers)
                await _notif.CreateAsync(driverId, "Rider demand — no driver available yet 🚗",
                    $"A rider needs {rideRequest.FromLocation} → {rideRequest.ToLocation} on {rideRequest.DesiredTime:dddd dd MMM 'around' h:mm tt}. Post a ride or add availability to take it.",
                    "RideRequest", rideRequest.RideRequestId);
        }

        return Ok(new { message = "Ride request created", rideRequestId = rideRequest.RideRequestId });
    }

    [HttpGet("requests/mine")]
    public async Task<IActionResult> MyRequests() =>
        Ok((await _db.RideRequests
            .Include(r => r.Rider)
            .Include(r => r.MatchedDriver)
            .Where(r => r.RiderId == Me)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync()).Select(RequestDto));

    [HttpGet("requests/open")]
    public async Task<IActionResult> OpenRequests() {
        if (Role != "Driver" && Role != "Admin") return Forbid();
        var driverAvailability = await _db.DriverAvailabilities
            .Where(a => a.DriverId == Me && a.IsActive && a.AvailableTo >= DateTime.UtcNow)
            .ToListAsync();

        var requests = await _db.RideRequests
            .Include(r => r.Rider)
            .Where(r => r.Status == "Open" && r.LatestTime >= DateTime.UtcNow)
            .OrderBy(r => r.DesiredTime)
            .ToListAsync();

        if (Role == "Driver" && driverAvailability.Count > 0) {
            requests = requests.Where(r => driverAvailability.Any(a =>
                a.Seats >= r.SeatsNeeded &&
                a.AvailableFrom <= r.LatestTime &&
                a.AvailableTo >= r.EarliestTime &&
                RouteMatch(a.FromLocation, r.FromLocation) &&
                RouteMatch(a.ToLocation, r.ToLocation))).ToList();
        }

        return Ok(requests.Select(RequestDto));
    }

    [HttpGet("requests/{id}/matches")]
    public async Task<IActionResult> Matches(int id) {
        var req = await _db.RideRequests.FindAsync(id);
        if (req == null || (req.RiderId != Me && Role != "Admin")) return NotFound();

        var availability = await _db.DriverAvailabilities
            .Include(a => a.Driver)
            .Where(a => a.IsActive && a.Seats >= req.SeatsNeeded && a.AvailableFrom <= req.LatestTime && a.AvailableTo >= req.EarliestTime)
            .ToListAsync();

        var matches = availability
            .Where(a => RouteMatch(a.FromLocation, req.FromLocation) && RouteMatch(a.ToLocation, req.ToLocation))
            .OrderBy(a => Math.Abs((a.AvailableFrom - req.DesiredTime).TotalMinutes))
            .ThenBy(a => a.SuggestedPrice ?? decimal.MaxValue)
            .Select(AvailabilityDto);

        return Ok(matches);
    }

    [HttpPut("requests/{id}/accept")]
    public async Task<IActionResult> AcceptRequest(int id, [FromBody] RideRequestActionRequest action) {
        if (Role != "Driver") return Forbid();
        var req = await _db.RideRequests.Include(r => r.Rider).FirstOrDefaultAsync(r => r.RideRequestId == id);
        if (req == null || req.Status != "Open") return NotFound();

        var driverSlots = await _db.DriverAvailabilities
            .Where(a => a.DriverId == Me && a.IsActive && a.Seats >= req.SeatsNeeded &&
                        a.AvailableFrom <= req.LatestTime && a.AvailableTo >= req.EarliestTime)
            .ToListAsync();
        var canServe = driverSlots.Any(a =>
            RouteMatch(a.FromLocation, req.FromLocation) && RouteMatch(a.ToLocation, req.ToLocation));

        if (!canServe) return BadRequest(new { message = "Create a matching availability slot before accepting this request" });

        // Close the loop: accepting a request creates a REAL ride + a confirmed
        // booking for the requesting rider — not just a status change.
        var slot   = driverSlots.First(a => RouteMatch(a.FromLocation, req.FromLocation) && RouteMatch(a.ToLocation, req.ToLocation));
        var driver = await _db.Users.FindAsync(Me);
        var car    = await _db.Cars.FirstOrDefaultAsync(c => c.DriverId == Me);
        var seats  = Math.Max(slot.Seats, req.SeatsNeeded);
        var price  = action.SuggestedPrice ?? slot.SuggestedPrice ?? req.MaxPrice ?? 5;

        var ride = new Ride {
            DriverId = Me, CarId = car?.CarId,
            FromLocation = req.FromLocation, ToLocation = req.ToLocation,
            FromLat = req.FromLat, FromLng = req.FromLng,
            ToLat = req.ToLat, ToLng = req.ToLng,
            DepartureTime = action.SuggestedTime ?? req.DesiredTime,
            TotalSeats = seats, AvailableSeats = seats - req.SeatsNeeded,
            PricePerSeat = price, GenderPreference = req.GenderPreference,
            Notes = req.Note, ShareToken = Guid.NewGuid().ToString("N")[..8], Status = "Upcoming"
        };
        _db.Rides.Add(ride);
        await _db.SaveChangesAsync();

        _db.Bookings.Add(new Booking { RideId = ride.RideId, RiderId = req.RiderId, SeatsBooked = req.SeatsNeeded, Note = req.Note, Status = "Confirmed" });
        req.Status = "Fulfilled";
        req.MatchedDriverId = Me;
        req.MatchedRideId = ride.RideId;
        req.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _notif.CreateAsync(req.RiderId, "Your ride request was accepted! ✓",
            $"{driver?.FullName ?? "A driver"} will drive you {req.FromLocation} → {req.ToLocation}. Seat confirmed.",
            "Confirmed", ride.RideId);
        await _email.SendBookingAcceptedAsync(
            req.Rider.Email, req.Rider.FullName,
            req.FromLocation, req.ToLocation,
            ride.DepartureTime.ToString("dddd dd MMM yyyy, h:mm tt"),
            driver?.FullName ?? "Driver", car?.PlateNumber ?? "—");

        return Ok(new { message = "Request accepted — ride created and rider booked", rideId = ride.RideId });
    }

    [HttpPut("requests/{id}/close")]
    public async Task<IActionResult> CloseRequest(int id) {
        var req = await _db.RideRequests.FindAsync(id);
        if (req == null || (req.RiderId != Me && Role != "Admin")) return NotFound();
        req.Status = "Closed";
        req.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Request closed" });
    }

    [HttpPost("availability")]
    public async Task<IActionResult> CreateAvailability([FromBody] DriverAvailabilityRequest req) {
        if (Role != "Driver") return Forbid();
        if (req.AvailableTo <= req.AvailableFrom) return BadRequest(new { message = "Available to must be after available from" });

        var slot = new DriverAvailability {
            DriverId = Me,
            FromLocation = req.FromLocation,
            ToLocation = req.ToLocation,
            AvailableFrom = req.AvailableFrom,
            AvailableTo = req.AvailableTo,
            Seats = Math.Max(1, req.Seats),
            SuggestedPrice = req.SuggestedPrice,
            Note = req.Note,
            IsActive = true
        };
        _db.DriverAvailabilities.Add(slot);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Availability saved", driverAvailabilityId = slot.DriverAvailabilityId });
    }

    [HttpGet("availability/mine")]
    public async Task<IActionResult> MyAvailability() =>
        Ok((await _db.DriverAvailabilities
            .Include(a => a.Driver)
            .Where(a => a.DriverId == Me && a.AvailableTo >= DateTime.UtcNow)
            .OrderBy(a => a.AvailableFrom)
            .ToListAsync()).Select(AvailabilityDto));

    [HttpPut("availability/{id}/disable")]
    public async Task<IActionResult> DisableAvailability(int id) {
        var slot = await _db.DriverAvailabilities.FindAsync(id);
        if (slot == null || slot.DriverId != Me) return NotFound();
        slot.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Availability disabled" });
    }
    [HttpGet("admin/open")]
    public async Task<IActionResult> AdminOpenDemand() {
        if (Role != "Admin") return Forbid();
        var now = DateTime.UtcNow;
        var requests = await _db.RideRequests
            .Include(r => r.Rider)
            .Where(r => r.Status == "Open" && r.LatestTime >= now)
            .OrderBy(r => r.DesiredTime)
            .ToListAsync();
        var availability = await _db.DriverAvailabilities
            .Where(a => a.IsActive && a.AvailableTo >= now)
            .ToListAsync();

        return Ok(requests.Select(r => new {
            requestId = r.RideRequestId,
            r.FromLocation,
            r.ToLocation,
            requestedTime = r.DesiredTime,
            r.SeatsNeeded,
            r.CreatedAt,
            availableDriverCount = availability
                .Where(a => a.Seats >= r.SeatsNeeded &&
                            a.AvailableFrom <= r.LatestTime &&
                            a.AvailableTo >= r.EarliestTime &&
                            RouteMatch(a.FromLocation, r.FromLocation) &&
                            RouteMatch(a.ToLocation, r.ToLocation))
                .Select(a => a.DriverId)
                .Distinct()
                .Count()
        }));
    }

    [HttpPost("admin/requests/{id}/notify")]
    public async Task<IActionResult> NotifyDemandDrivers(int id) {
        if (Role != "Admin") return Forbid();
        var req = await _db.RideRequests.FirstOrDefaultAsync(r => r.RideRequestId == id && r.Status == "Open");
        if (req == null) return NotFound(new { message = "Open request not found" });

        var slots = await _db.DriverAvailabilities
            .Where(a => a.IsActive && a.AvailableTo >= DateTime.UtcNow && a.Seats >= req.SeatsNeeded &&
                        a.AvailableFrom <= req.LatestTime && a.AvailableTo >= req.EarliestTime)
            .ToListAsync();
        var driverIds = slots
            .Where(a => RouteMatch(a.FromLocation, req.FromLocation) && RouteMatch(a.ToLocation, req.ToLocation))
            .Select(a => a.DriverId)
            .Distinct()
            .ToList();

        foreach (var driverId in driverIds)
            await _notif.CreateAsync(driverId, "Rider request needs a driver",
                $"{req.FromLocation} -> {req.ToLocation}, {req.DesiredTime:ddd dd MMM h:mm tt}",
                "RideRequest", req.RideRequestId);

        return Ok(new { message = $"Notified {driverIds.Count} drivers", notified = driverIds.Count, notifiedCount = driverIds.Count });
    }
    [HttpGet("admin/analytics")]
    public async Task<IActionResult> DemandAnalytics() {
        if (Role != "Admin") return Forbid();
        var now = DateTime.UtcNow;
        var requests = await _db.RideRequests.Where(r => r.CreatedAt >= now.AddDays(-30)).ToListAsync();
        var availability = await _db.DriverAvailabilities.Where(a => a.CreatedAt >= now.AddDays(-30)).ToListAsync();

        // Supply coverage per (day-of-week, hour): walk each active slot hour by
        // hour so a Mon 16:00–22:00 slot counts for Mon 16..22 only — not for the
        // same hours on other days. Handles midnight-crossing slots too.
        var supplyMap = new Dictionary<(int dow, int hour), int>();
        foreach (var a in availability.Where(x => x.IsActive)) {
            var t = new DateTime(a.AvailableFrom.Year, a.AvailableFrom.Month, a.AvailableFrom.Day, a.AvailableFrom.Hour, 0, 0);
            int guard = 0;
            while (t <= a.AvailableTo && guard++ < 96) {          // cap 4 days per slot
                var key = ((int)t.DayOfWeek, t.Hour);
                supplyMap[key] = supplyMap.GetValueOrDefault(key) + 1;
                t = t.AddHours(1);
            }
        }

        // Demand per (day-of-week, hour) from the request's desired time — two
        // requests at 07:00 on DIFFERENT weekdays land in different buckets.
        string[] dayNames = ["Sunday","Monday","Tuesday","Wednesday","Thursday","Friday","Saturday"];
        int[] dowOrder = [1, 2, 3, 4, 5, 6, 0];                    // display Monday → Sunday
        var days = dowOrder.Select(dow => new {
            dow,
            day = dayNames[dow],
            hours = Enumerable.Range(0, 24).Select(hour => {
                var demand = requests.Count(r => r.Status != "Closed" && (int)r.DesiredTime.DayOfWeek == dow && r.DesiredTime.Hour == hour);
                var supply = supplyMap.GetValueOrDefault((dow, hour));
                return new {
                    hour, label = $"{hour:00}:00", demand, supply, gap = demand - supply,
                    recommendation = demand > supply ? $"Recommend drivers to post {dayNames[dow]} around {hour:00}:00" : "Supply is enough"
                };
            }).ToList()
        }).ToList();

        var flat = days.SelectMany(d => d.hours.Select(h => new { d.day, h.hour, h.label, h.demand, h.supply, h.gap, h.recommendation })).ToList();

        var topRoutes = requests
            .GroupBy(r => new { r.FromLocation, r.ToLocation })
            .Select(g => new { g.Key.FromLocation, g.Key.ToLocation, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(5);

        return Ok(new {
            openRequests = requests.Count(r => r.Status == "Open"),
            acceptedRequests = requests.Count(r => r.Status == "DriverAccepted" || r.Status == "Fulfilled"),
            activeAvailability = availability.Count(a => a.IsActive && a.AvailableTo >= now),
            biggestGap = flat.OrderByDescending(s => s.gap).FirstOrDefault(),
            days,
            topRoutes
        });
    }
}
