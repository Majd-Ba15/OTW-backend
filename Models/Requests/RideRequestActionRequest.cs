namespace OTW.Api.Models;

public record RideRequestActionRequest(string? Note = null, decimal? SuggestedPrice = null, DateTime? SuggestedTime = null);

