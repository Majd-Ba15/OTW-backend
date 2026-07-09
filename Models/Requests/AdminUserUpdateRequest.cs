namespace OTW.Api.Models;

public record AdminUserUpdateRequest(
    string? FullName = null,
    string? Email = null,
    string? StudentId = null,
    string? Faculty = null,
    string? Phone = null,
    string? Gender = null,
    string? Role = null,
    string? University = null,
    string? CampusName = null,
    bool? IsActive = null,
    bool? IsVerified = null,
    bool? IsEmailVerified = null
);