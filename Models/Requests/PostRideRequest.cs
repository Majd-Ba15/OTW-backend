namespace OTW.Api.Models;

public class PostRideRequest {
    public string    FromLocation     { get; set; } = "";
    public string    ToLocation       { get; set; } = "";
    public decimal?  FromLat          { get; set; }
    public decimal?  FromLng          { get; set; }
    public decimal?  ToLat            { get; set; }
    public decimal?  ToLng            { get; set; }
    public decimal?  DistanceKm       { get; set; }
    public int?      DurationMin      { get; set; }
    public DateTime  DepartureTime    { get; set; }
    public int       TotalSeats       { get; set; }
    public decimal   PricePerSeat     { get; set; }
    public string    GenderPreference { get; set; } = "Any";
    public string?   Notes            { get; set; }
    public bool      IsRecurring      { get; set; }
    public string?   RecurringDays    { get; set; }
    public DateTime? RecurringEndDate { get; set; }
    public string?   Status           { get; set; }   // partial updates (pause/resume)
    public List<StopDto> Stops        { get; set; } = [];
}

