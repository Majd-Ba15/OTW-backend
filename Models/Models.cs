namespace OTW.Api.Models;

// ── Config models ─────────────────────────────
public class AppSettings {
    public bool     RequireUniversityEmail    { get; set; }
    public bool     RequireOTP                { get; set; }
    public bool     AutoApproveVerification   { get; set; }
    public string[] AllowedEmailDomains       { get; set; } = [];
    public string   JwtSecret                 { get; set; } = "";
    public int      JwtExpiryDays             { get; set; } = 7;
    public string   AdminEmail                { get; set; } = "";
    public string   AdminPassword             { get; set; } = "";
}
public class MailSettings { public string Email{get;set;}=""; public string AppPassword{get;set;}=""; public string DisplayName{get;set;}=""; }

// ── DB models ─────────────────────────────────
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
    public DateTime  CreatedAt         { get; set; } = DateTime.UtcNow;
    public ICollection<Ride>    Rides    { get; set; } = [];
    public ICollection<Booking> Bookings { get; set; } = [];
}
public class Car {
    public int      CarId          { get; set; }
    public int      DriverId       { get; set; }
    public string   Model          { get; set; } = "";
    public string   Colour         { get; set; } = "";
    public string   PlateNumber    { get; set; } = "";
    public int      TotalSeats     { get; set; } = 4;
    public string?  CarPhotoFront  { get; set; }
    public string?  CarPhotoSide   { get; set; }
    public string?  LicencePhoto   { get; set; }
    public DateTime CreatedAt      { get; set; } = DateTime.UtcNow;
    public User     Driver         { get; set; } = null!;
}
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
public class Waitlist {
    public int      WaitlistId { get; set; }
    public int      RideId     { get; set; }
    public int      RiderId    { get; set; }
    public int      Position   { get; set; }
    public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;
    public Ride     Ride       { get; set; } = null!;
    public User     Rider      { get; set; } = null!;
}
public class Message {
    public int      MessageId   { get; set; }
    public int      RideId      { get; set; }
    public int      SenderId    { get; set; }
    public int?     ReceiverId  { get; set; }
    public string   Content     { get; set; } = "";
    public bool     IsBroadcast { get; set; }
    public bool     IsRead      { get; set; }
    public DateTime SentAt      { get; set; } = DateTime.UtcNow;
    public Ride     Ride        { get; set; } = null!;
    public User     Sender      { get; set; } = null!;
}
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
public class Report {
    public int       ReportId     { get; set; }
    public int       ReportedBy   { get; set; }
    public int       ReportedUser { get; set; }
    public int?      RideId       { get; set; }
    public string    Type         { get; set; } = "";
    public string    Statement    { get; set; } = "";
    public string    Status       { get; set; } = "Open";
    public string?   AdminNote    { get; set; }
    public string?   ActionTaken  { get; set; }
    public DateTime  CreatedAt    { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt   { get; set; }
}
public class ChatLog {
    public int      LogId       { get; set; }
    public int      UserId      { get; set; }
    public string   UserMessage { get; set; } = "";
    public string   AIResponse  { get; set; } = "";
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public User     User        { get; set; } = null!;
}
public class Notification {
    public int      NotificationId { get; set; }
    public int      UserId         { get; set; }
    public string   Title          { get; set; } = "";
    public string   Body           { get; set; } = "";
    public string   Type           { get; set; } = "";
    public int?     RelatedId      { get; set; }
    public bool     IsRead         { get; set; }
    public DateTime CreatedAt      { get; set; } = DateTime.UtcNow;
    public User     User           { get; set; } = null!;
}
public class FavouriteRoute {
    public int      FavouriteId  { get; set; }
    public int      UserId       { get; set; }
    public string   FromLocation { get; set; } = "";
    public string   ToLocation   { get; set; } = "";
    public int      UseCount     { get; set; } = 1;
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    public User     User         { get; set; } = null!;
}
public class LocationHistory {
    public int      LocationId { get; set; }
    public int      RideId     { get; set; }
    public int      DriverId   { get; set; }
    public decimal  Lat        { get; set; }
    public decimal  Lng        { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public Ride     Ride       { get; set; } = null!;
}

// ── DTOs ──────────────────────────────────────
public record RegisterRequest(string FullName, string Email, string Password, string Role = "Rider");
public record LoginRequest(string Email, string Password);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Password);
public record OtpRequest(int UserId, string OtpCode);
public record BookingRequest(int RideId, int SeatsBooked = 1, string? Note = null);
public record RatingRequest(int BookingId, int RatedUserId, int Stars, string? Tags, string? Comment);
public record ChatRequest(string Message);
public record ReportRequest(int ReportedUser, int? RideId, string Type, string Statement);
public record AdminActionRequest(string? Note = null, string? Reason = null);
public record LocationUpdate(int RideId, decimal Lat, decimal Lng);
public class PostRideRequest {
    public string    FromLocation     { get; set; } = "";
    public string    ToLocation       { get; set; } = "";
    public decimal?  FromLat          { get; set; }
    public decimal?  FromLng          { get; set; }
    public decimal?  ToLat            { get; set; }
    public decimal?  ToLng            { get; set; }
    public DateTime  DepartureTime    { get; set; }
    public int       TotalSeats       { get; set; }
    public decimal   PricePerSeat     { get; set; }
    public string    GenderPreference { get; set; } = "Any";
    public string?   Notes            { get; set; }
    public bool      IsRecurring      { get; set; }
    public string?   RecurringDays    { get; set; }
    public DateTime? RecurringEndDate { get; set; }
    public List<StopDto> Stops        { get; set; } = [];
}
public record StopDto(string StopName, decimal? StopLat, decimal? StopLng, decimal StopPrice, int StopOrder);
public class ServiceResult {
    public bool   Success  { get; set; }
    public string Message  { get; set; } = "";
    public string Token    { get; set; } = "";
    public int    UserId   { get; set; }
    public string Role     { get; set; } = "";
    public string FullName { get; set; } = "";
}
