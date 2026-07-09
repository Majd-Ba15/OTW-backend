namespace OTW.Api.Models;

public record RatingRequest(int BookingId, int RatedUserId, int Stars, string? Tags, string? Comment);

