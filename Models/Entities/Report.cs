namespace OTW.Api.Models;

public class Report {
    public int       ReportId     { get; set; }
    public int       ReportedBy   { get; set; }
    public int       ReportedUser { get; set; }
    public int?      RideId       { get; set; }
    public string    Type         { get; set; } = "";
    public string    Statement    { get; set; } = "";
    public string    Status       { get; set; } = "Open";
    public string?   AdminNote    { get; set; }
    public string?   ActionTaken  { get; set; }
    public DateTime  CreatedAt    { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt   { get; set; }
}

