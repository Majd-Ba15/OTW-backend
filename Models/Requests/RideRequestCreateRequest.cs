namespace OTW.Api.Models;

public record RideRequestCreateRequest(string FromLocation, string ToLocation, DateTime DesiredTime, DateTime? EarliestTime, DateTime? LatestTime, int SeatsNeeded = 1, decimal? MaxPrice = null, string GenderPreference = "Any", string? Note = null, decimal? FromLat = null, decimal? FromLng = null, decimal? ToLat = null, decimal? ToLng = null);