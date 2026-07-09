namespace OTW.Api.Models;

public record RegisterRequest(string FullName, string Email, string Password, string Role = "Rider", string? University = null);

