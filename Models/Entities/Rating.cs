namespace OTW.Api.Models;

public class Rating {
    public int      RatingId    { get; set; }
    public int      BookingId   { get; set; }
    public int      RaterId     { get; set; }
    public int      RatedUserId { get; set; }
    public int      Stars       { get; set; }
    public string?  Tags        { get; set; }
    public string?  Comment     { get; set; }
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public Booking  Booking     { get; set; } = null!;
}

