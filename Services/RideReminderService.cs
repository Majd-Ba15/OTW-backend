using Microsoft.EntityFrameworkCore;
using OTW.Api.Data;
using OTW.Api.Services;

namespace OTW.Api.Services;

// ── RIDE REMINDER BACKGROUND SERVICE ─────────
// Runs every 5 minutes, sends email 1 hour before departure
// to all confirmed passengers on upcoming rides
public class RideReminderService : BackgroundService
{
    readonly IServiceScopeFactory _factory;
    readonly ILogger<RideReminderService> _logger;

    public RideReminderService(IServiceScopeFactory factory, ILogger<RideReminderService> logger)
    {
        _factory = factory;
        _logger  = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Wait 30 seconds after startup before first run
        await Task.Delay(TimeSpan.FromSeconds(30), ct);

        while (!ct.IsCancellationRequested)
        {
            try { await SendReminders(); }
            catch (Exception ex) { _logger.LogError(ex, "Ride reminder error"); }

            // Run every 5 minutes
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }

    async Task SendReminders()
    {
        using var scope = _factory.CreateScope();
        var db    = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();

        // Find rides departing in 55–65 minutes (5-min window to avoid double-sending)
        var windowStart = DateTime.UtcNow.AddMinutes(55);
        var windowEnd   = DateTime.UtcNow.AddMinutes(65);

        var rides = await db.Rides
            .Include(r => r.Driver)
            .Include(r => r.Car)
            .Include(r => r.Bookings).ThenInclude(b => b.Rider)
            .Where(r =>
                r.Status == "Upcoming" &&
                r.DepartureTime >= windowStart &&
                r.DepartureTime <= windowEnd)
            .ToListAsync();

        foreach (var ride in rides)
        {
            var confirmed = ride.Bookings.Where(b => b.Status == "Confirmed").ToList();
            foreach (var booking in confirmed)
            {
                _logger.LogInformation(
                    "Sending ride reminder to {Email} for ride {RideId}",
                    booking.Rider.Email, ride.RideId);

                await email.SendRideReminderAsync(
                    booking.Rider.Email,
                    booking.Rider.FullName,
                    ride.FromLocation,
                    ride.ToLocation,
                    ride.DepartureTime.ToString("dddd dd MMM yyyy, h:mm tt"),
                    ride.Driver.FullName,
                    ride.Car?.PlateNumber ?? "—",
                    ride.Car?.Model ?? "—");
            }
        }
    }
}
