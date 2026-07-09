namespace OTW.Api.Models;

public class Booking {
    public int       BookingId   { get; set; }
    public int       RideId      { get; set; }
    public int       RiderId     { get; set; }
    public int       SeatsBooked { get; set; } = 1;
    public string?   Note        { get; set; }
    public string    Status      { get; set; } = "Pending";
    public DateTime  CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt   { get; set; }
    public Ride      Ride        { get; set; } = null!;
    public User      Rider       { get; set; } = null!;
}

