namespace OTW.Api.Models;

public class LocationHistory {
    public int      LocationId { get; set; }
    public int      RideId     { get; set; }
    public int      DriverId   { get; set; }
    public decimal  Lat        { get; set; }
    public decimal  Lng        { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public Ride     Ride       { get; set; } = null!;
}

