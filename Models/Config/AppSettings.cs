namespace OTW.Api.Models;

public class AppSettings {
    public bool     RequireUniversityEmail    { get; set; }
    public bool     RequireOTP                { get; set; }
    public bool     AutoApproveVerification   { get; set; }
    public string[] AllowedEmailDomains       { get; set; } = [];
    public string   JwtSecret                 { get; set; } = "";
    public int      JwtExpiryDays             { get; set; } = 7;
    public string   AdminEmail                { get; set; } = "";
    public string   AdminPassword             { get; set; } = "";
}

