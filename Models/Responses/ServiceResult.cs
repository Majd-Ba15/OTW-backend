namespace OTW.Api.Models;

public class ServiceResult {
    public bool   Success  { get; set; }
    public string Message  { get; set; } = "";
    public string Token    { get; set; } = "";
    public int    UserId   { get; set; }
    public string Role     { get; set; } = "";
    public string FullName { get; set; } = "";
}

