using Microsoft.EntityFrameworkCore;
using OTW.Api.Models;
namespace OTW.Api.Data;

public class AppDbContext : DbContext {
    public AppDbContext(DbContextOptions<AppDbContext> o) : base(o) {}
    public DbSet<User>            Users            { get; set; }
    public DbSet<Car>             Cars             { get; set; }
    public DbSet<Ride>            Rides            { get; set; }
    public DbSet<RideStop>        RideStops        { get; set; }
    public DbSet<Booking>         Bookings         { get; set; }
    public DbSet<Message>         Messages         { get; set; }
    public DbSet<Rating>          Ratings          { get; set; }
    public DbSet<Report>          Reports          { get; set; }
    public DbSet<ChatLog>         ChatLogs         { get; set; }
    public DbSet<Notification>    Notifications    { get; set; }
    public DbSet<FavouriteRoute>  FavouriteRoutes  { get; set; }
    public DbSet<LocationHistory> LocationHistory  { get; set; }
    public DbSet<RideRequest>     RideRequests     { get; set; }
    public DbSet<DriverAvailability> DriverAvailabilities { get; set; }

    protected override void OnModelCreating(ModelBuilder m) {
        m.Entity<User>().HasIndex(u => u.Email).IsUnique();
        m.Entity<Car>().HasOne(c => c.Driver).WithMany().HasForeignKey(c => c.DriverId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<Ride>().HasOne(r => r.Driver).WithMany(u => u.Rides).HasForeignKey(r => r.DriverId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Ride>().HasOne(r => r.Car).WithMany().HasForeignKey(r => r.CarId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<RideStop>().HasOne(s => s.Ride).WithMany(r => r.Stops).HasForeignKey(s => s.RideId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<Booking>().HasOne(b => b.Ride).WithMany(r => r.Bookings).HasForeignKey(b => b.RideId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Booking>().HasOne(b => b.Rider).WithMany(u => u.Bookings).HasForeignKey(b => b.RiderId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Message>().HasOne(msg => msg.Ride).WithMany(r => r.Messages).HasForeignKey(msg => msg.RideId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<Message>().HasOne(msg => msg.Sender).WithMany().HasForeignKey(msg => msg.SenderId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<RideStop>().HasKey(s => s.StopId);
        m.Entity<FavouriteRoute>().HasKey(f => f.FavouriteId);
        m.Entity<FavouriteRoute>().HasOne(f => f.User).WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<LocationHistory>().HasKey(l => l.LocationId);
        m.Entity<LocationHistory>().HasOne(l => l.Ride).WithMany(r => r.Locations).HasForeignKey(l => l.RideId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<RideRequest>().HasKey(r => r.RideRequestId);
        m.Entity<RideRequest>().HasOne(r => r.Rider).WithMany().HasForeignKey(r => r.RiderId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<RideRequest>().HasOne(r => r.MatchedDriver).WithMany().HasForeignKey(r => r.MatchedDriverId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<DriverAvailability>().HasKey(a => a.DriverAvailabilityId);
        m.Entity<DriverAvailability>().HasOne(a => a.Driver).WithMany().HasForeignKey(a => a.DriverId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<ChatLog>().HasKey(c => c.LogId);
        m.Entity<ChatLog>().HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<User>().Property(u => u.AverageRating).HasColumnType("decimal(3,2)");
        m.Entity<Ride>().Property(r => r.PricePerSeat).HasColumnType("decimal(8,2)");
        m.Entity<Ride>().Property(r => r.DistanceKm).HasColumnType("decimal(8,2)");
        m.Entity<Ride>().Property(r => r.FromLat).HasColumnType("decimal(10,7)");
        m.Entity<Ride>().Property(r => r.FromLng).HasColumnType("decimal(10,7)");
        m.Entity<Ride>().Property(r => r.ToLat).HasColumnType("decimal(10,7)");
        m.Entity<Ride>().Property(r => r.ToLng).HasColumnType("decimal(10,7)");
        m.Entity<Ride>().Property(r => r.CurrentLat).HasColumnType("decimal(10,7)");
        m.Entity<Ride>().Property(r => r.CurrentLng).HasColumnType("decimal(10,7)");
        m.Entity<RideStop>().Property(s => s.StopPrice).HasColumnType("decimal(8,2)");
        m.Entity<LocationHistory>().Property(l => l.Lat).HasColumnType("decimal(10,7)");
        m.Entity<LocationHistory>().Property(l => l.Lng).HasColumnType("decimal(10,7)");
        m.Entity<RideRequest>().Property(r => r.MaxPrice).HasColumnType("decimal(8,2)");
        m.Entity<RideRequest>().Property(r => r.FromLat).HasColumnType("decimal(10,7)");
        m.Entity<RideRequest>().Property(r => r.FromLng).HasColumnType("decimal(10,7)");
        m.Entity<RideRequest>().Property(r => r.ToLat).HasColumnType("decimal(10,7)");
        m.Entity<RideRequest>().Property(r => r.ToLng).HasColumnType("decimal(10,7)");
        m.Entity<DriverAvailability>().Property(a => a.SuggestedPrice).HasColumnType("decimal(8,2)");
    }
}
