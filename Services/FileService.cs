using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MailKit.Net.Smtp;
using MimeKit;
using OTW.Api.Data;
using OTW.Api.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace OTW.Api.Services;

public class FileService : IFileService {
    readonly IWebHostEnvironment _env;
    public FileService(IWebHostEnvironment env) => _env = env;
    public async Task<string> SaveAsync(IFormFile file, string folder) {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploadDir = Path.Combine(webRoot, "uploads", folder);
        Directory.CreateDirectory(uploadDir);
        var ext  = Path.GetExtension(file.FileName);
        var name = $"{Guid.NewGuid()}{ext}";
        var path = Path.Combine(uploadDir, name);
        using var stream = new FileStream(path, FileMode.Create);
        await file.CopyToAsync(stream);
        return $"http://localhost:5000/uploads/{folder}/{name}";
    }
}

