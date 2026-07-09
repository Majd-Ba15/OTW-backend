namespace OTW.Api.Models;

public record SendMessageRequest(int RideId, int? ReceiverId, string Content, bool IsBroadcast = false);

