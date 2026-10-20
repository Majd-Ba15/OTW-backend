using Microsoft.Extensions.Options;
using OTW.Api.Models;
using System.Text.Json;

namespace OTW.Api.Services;

public class FileService : IFileService {
    readonly IWebHostEnvironment _env;
    readonly IHttpClientFactory _http;
    readonly ImgBbSettings _imgBb;

    public FileService(IWebHostEnvironment env, IHttpClientFactory http, IOptions<ImgBbSettings> imgBb) {
        _env = env; _http = http; _imgBb = imgBb.Value;
    }

    const long MaxBytes = 10 * 1024 * 1024;   // 10 MB cap

    // Identify the real image type from its magic bytes (not the client-supplied
    // filename) and return a safe extension, or null if it isn't a known image.
    // This blocks non-images and executable content (e.g. .html/.svg) being
    // uploaded and later served from the public /uploads path.
    static string? DetectImageExtension(byte[] b) {
        if (b.Length >= 3  && b[0]==0xFF && b[1]==0xD8 && b[2]==0xFF) return ".jpg";
        if (b.Length >= 8  && b[0]==0x89 && b[1]==0x50 && b[2]==0x4E && b[3]==0x47 && b[4]==0x0D && b[5]==0x0A && b[6]==0x1A && b[7]==0x0A) return ".png";
        if (b.Length >= 6  && b[0]==0x47 && b[1]==0x49 && b[2]==0x46 && b[3]==0x38 && (b[4]==0x37 || b[4]==0x39) && b[5]==0x61) return ".gif";
        if (b.Length >= 12 && b[0]==0x52 && b[1]==0x49 && b[2]==0x46 && b[3]==0x46 && b[8]==0x57 && b[9]==0x45 && b[10]==0x42 && b[11]==0x50) return ".webp";
        return null;
    }

    public async Task<string> SaveAsync(IFormFile file, string folder) {
        if (file == null || file.Length == 0) throw new InvalidOperationException("No file provided");
        if (file.Length > MaxBytes)           throw new InvalidOperationException("File too large (max 10 MB)");

        // Read the content so we can verify what it actually is before writing it.
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var ext = DetectImageExtension(bytes)
            ?? throw new InvalidOperationException("Only JPG, PNG, GIF or WEBP images are allowed");

        // Hosted deployments (Render, etc.) have an ephemeral disk, so uploads
        // are pushed to ImgBB and served from its CDN instead of local storage.
        // Falls back to local disk only when no ImgBB key is configured (local dev).
        if (!string.IsNullOrWhiteSpace(_imgBb.ApiKey))
            return await UploadToImgBbAsync(bytes, _imgBb.ApiKey);

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploadDir = Path.Combine(webRoot, "uploads", folder);
        Directory.CreateDirectory(uploadDir);
        // Server-controlled name + extension — the client filename never reaches disk.
        var name = $"{Guid.NewGuid()}{ext}";
        var path = Path.Combine(uploadDir, name);
        await System.IO.File.WriteAllBytesAsync(path, bytes);
        return $"http://localhost:5000/uploads/{folder}/{name}";
    }

    async Task<string> UploadToImgBbAsync(byte[] bytes, string apiKey) {
        using var client = _http.CreateClient();
        using var content = new MultipartFormDataContent {
            { new StringContent(Convert.ToBase64String(bytes)), "image" }
        };
        var res = await client.PostAsync($"https://api.imgbb.com/1/upload?key={apiKey}", content);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").GetProperty("url").GetString()!;
    }
}
