using System.Security.Claims;
using VIIDII.Models;

namespace VIIDII.Services;

/// <summary>
/// Well-known claim types used by VIIDII authentication.
/// Kept in one place so the login endpoint, the SignalR hub and the UI agree.
/// </summary>
public static class ViidiiClaims
{
    public const string UserId = "viidii:userid";
    public const string MatricNo = "viidii:matricno";
    public const string DisplayName = "viidii:name";
    public const string Department = "viidii:department";
    public const string Level = "viidii:level";
}

/// <summary>
/// Authorization policy names.
/// </summary>
public static class ViidiiPolicies
{
    public const string LecturerOnly = "LecturerOnly";
    public const string AdminOnly = "AdminOnly";
    public const string StaffOrAdmin = "StaffOrAdmin";
}

/// <summary>
/// Reads the authenticated user from the current HttpContext claims.
/// This is the single source of truth for "who is this request?" inside the
/// server, including SignalR hub calls.
/// </summary>
public sealed class CurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserService _userService;

    public CurrentUserAccessor(IHttpContextAccessor httpContextAccessor, UserService userService)
    {
        _httpContextAccessor = httpContextAccessor;
        _userService = userService;
    }

    public ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? MatricNo => Principal?.FindFirst(ViidiiClaims.MatricNo)?.Value;

    public Role? Role
    {
        get
        {
            var value = Principal?.FindFirst(ClaimTypes.Role)?.Value;
            return Enum.TryParse<Role>(value, out var role) ? role : null;
        }
    }

    public string? DisplayName => Principal?.FindFirst(ViidiiClaims.DisplayName)?.Value;

    /// <summary>
    /// Loads the full user record for the current request, or null when anonymous.
    /// </summary>
    public async Task<User?> GetUserAsync()
    {
        var matricNo = MatricNo;
        if (string.IsNullOrEmpty(matricNo))
        {
            return null;
        }

        return await _userService.GetUserByMatricNoAsync(matricNo);
    }
}

/// <summary>
/// Builds the ClaimsPrincipal issued on successful login.
/// </summary>
public static class ViidiiPrincipalBuilder
{
    public static ClaimsPrincipal Build(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(ViidiiClaims.UserId, user.Id.ToString()),
            new(ViidiiClaims.MatricNo, user.MatricNo),
            new(ViidiiClaims.DisplayName, user.Name),
        };

        if (user.Department.HasValue && user.Department != User.Departments.Any)
        {
            claims.Add(new Claim(ViidiiClaims.Department, user.Department.Value.ToString()));
        }

        if (user.Level.HasValue && user.Level != User.Levels.Any)
        {
            claims.Add(new Claim(ViidiiClaims.Level, user.Level.Value.ToString()));
        }

        var identity = new ClaimsIdentity(claims, "VIIDII");
        return new ClaimsPrincipal(identity);
    }
}