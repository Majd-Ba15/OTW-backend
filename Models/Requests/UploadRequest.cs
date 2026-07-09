namespace OTW.Api.Models;

public class UploadRequest {
    public string base64 { get; set; } = "";
    public string? name { get; set; }
    public string? type { get; set; }
}

