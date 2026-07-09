namespace OTW.Api.Models;

public record DriverAvailabilityRequest(string FromLocation, string ToLocation, DateTime AvailableFrom, DateTime AvailableTo, int Seats = 1, decimal? SuggestedPrice = null, string? Note = null);

