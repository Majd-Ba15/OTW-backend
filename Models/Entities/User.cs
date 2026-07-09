namespace OTW.Api.Models;

public class User {
    public int       UserId            { get; set; }
    public string    FullName          { get; set; } = "";
    public string    Email             { get; set; } = "";
    public string    PasswordHash      { get; set; } = "";
    public string?   StudentId         { get; set; }
    public string?   Faculty           { get; set; }
    public string?   Phone             { get; set; }
    public string?   Gender            { get; set; }
    public string    Role              { get; set; } = "Rider";
    public string?   ProfilePhoto      { get; set; }
    public string?   StudentIdPhoto    { get; set; }
    public bool      IsEmailVerified   { get; set; }
    public bool      IsVerified        { get; set; }
    public bool      IsActive          { get; set; } = true;
    public decimal   AverageRating     { get; set; }
    public string?   OtpCode           { get; set; }
    public DateTime? OtpExpiry         { get; set; }
    public string?   FcmToken          { get; set; }
    public string?   EmergencyContact  { get; set; }
    public string?   EmergencyPhone    { get; set; }
    public bool      IsAvailable       { get; set; } = true;
    public int       ProfileCompletion { get; set; }
    public string?   University        { get; set; }   // university code, e.g. "LAU"; null = unset (treated as Other in UI)
    public string?   CampusName        { get; set; }   // optional home campus, e.g. "LAU Beirut Campus"
    public DateTime  CreatedAt         { get; set; } = DateTime.UtcNow;
    public ICollection<Ride>    Rides    { get; set; } = [];
    public ICollection<Booking> Bookings { get; set; } = [];
}

