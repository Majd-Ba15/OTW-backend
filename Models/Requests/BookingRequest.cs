namespace OTW.Api.Models;

public record BookingRequest(int RideId, int SeatsBooked = 1, string? Note = null);

