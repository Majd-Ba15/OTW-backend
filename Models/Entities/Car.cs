namespace OTW.Api.Models;

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

