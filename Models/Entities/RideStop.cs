namespace OTW.Api.Models;

public class RideStop {
    public int      StopId    { get; set; }
    public int      RideId    { get; set; }
    public string   StopName  { get; set; } = "";
    public decimal? StopLat   { get; set; }
    public decimal? StopLng   { get; set; }
    public decimal  StopPrice { get; set; }
    public int      StopOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Ride     Ride      { get; set; } = null!;
}

