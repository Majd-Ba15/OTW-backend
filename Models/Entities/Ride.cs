namespace OTW.Api.Models;

public class Ride {
    public int       RideId           { get; set; }
    public int       DriverId         { get; set; }
    public int?      CarId            { get; set; }
    public string    FromLocation     { get; set; } = "";
    public string    ToLocation       { get; set; } = "";
    public decimal?  FromLat          { get; set; }
    public decimal?  FromLng          { get; set; }
    public decimal?  ToLat            { get; set; }
    public decimal?  ToLng            { get; set; }
    public decimal?  CurrentLat       { get; set; }
    public decimal?  CurrentLng       { get; set; }
    public decimal?  DistanceKm       { get; set; }   // real road distance from OSRM (frontend)
    public int?      DurationMin      { get; set; }   // estimated driving minutes from OSRM
    public DateTime  DepartureTime    { get; set; }
    public int       AvailableSeats   { get; set; }
    public int       TotalSeats       { get; set; }
    public decimal   PricePerSeat     { get; set; }
    public string    GenderPreference { get; set; } = "Any";
    public string?   Notes            { get; set; }
    public string    Status           { get; set; } = "Upcoming";
    public bool      IsRecurring      { get; set; }
    public string?   RecurringDays    { get; set; }
    public DateTime? RecurringEndDate { get; set; }
    public string?   ShareToken       { get; set; }
    public DateTime  CreatedAt        { get; set; } = DateTime.UtcNow;
    public User      Driver           { get; set; } = null!;
    public Car?      Car              { get; set; }
    public ICollection<Booking>      Bookings { get; set; } = [];
    public ICollection<Message>      Messages { get; set; } = [];
    public ICollection<RideStop>     Stops    { get; set; } = [];
    public ICollection<LocationHistory> Locations { get; set; } = [];
}

