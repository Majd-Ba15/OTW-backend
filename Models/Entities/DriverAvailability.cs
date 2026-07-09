namespace OTW.Api.Models;

public class DriverAvailability {
    public int      DriverAvailabilityId { get; set; }
    public int      DriverId      { get; set; }
    public string   FromLocation  { get; set; } = "";
    public string   ToLocation    { get; set; } = "";
    public DateTime AvailableFrom { get; set; }
    public DateTime AvailableTo   { get; set; }
    public int      Seats         { get; set; } = 1;
    public decimal? SuggestedPrice { get; set; }
    public string?  Note          { get; set; }
    public bool     IsActive      { get; set; } = true;
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
    public User     Driver        { get; set; } = null!;
}

