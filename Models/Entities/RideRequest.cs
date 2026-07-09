namespace OTW.Api.Models;

public class RideRequest {
    public int      RideRequestId { get; set; }
    public int      RiderId       { get; set; }
    public string   FromLocation  { get; set; } = "";
    public string   ToLocation    { get; set; } = "";
    public decimal? FromLat      { get; set; }
    public decimal? FromLng      { get; set; }
    public decimal? ToLat        { get; set; }
    public decimal? ToLng        { get; set; }
    public DateTime DesiredTime   { get; set; }
    public DateTime EarliestTime  { get; set; }
    public DateTime LatestTime    { get; set; }
    public int      SeatsNeeded   { get; set; } = 1;
    public decimal? MaxPrice      { get; set; }
    public string   GenderPreference { get; set; } = "Any";
    public string?  Note          { get; set; }
    public string   Status        { get; set; } = "Open";
    public int?     MatchedDriverId { get; set; }
    public int?     MatchedRideId { get; set; }
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt    { get; set; }
    public User     Rider         { get; set; } = null!;
    public User?    MatchedDriver { get; set; }
}

