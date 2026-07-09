namespace OTW.Api.Models;

public record CarRequest(string Model, string Colour, string PlateNumber, int TotalSeats = 4);

