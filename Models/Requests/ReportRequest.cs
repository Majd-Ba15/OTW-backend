namespace OTW.Api.Models;

public record ReportRequest(int ReportedUser, int? RideId, string Type, string Statement);

