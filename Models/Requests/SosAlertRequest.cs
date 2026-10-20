namespace OTW.Api.Models;

// Sent by the rider SOS page. MapsUrl is null when the browser couldn't get a
// GPS fix — the email then falls back to a "needs help, in danger" message.
public record SosAlertRequest(string ToEmail, string ToName, string FromName, string? MapsUrl);
