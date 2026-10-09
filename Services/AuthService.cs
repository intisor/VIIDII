using Microsoft.AspNetCore.Identity;
using VIIDII.Models;

namespace VIIDII.Services;

/// <summary>
/// Authentication service for VIIDII.
///
/// IMPORTANT: this class validates credentials and builds claims. It does NOT
/// issue the authentication cookie — that happens in the /auth/login endpoint
/// where an HttpContext is guaranteed to be available. Cookie writes are not
/// possible from inside an established Blazor Server circuit.
/// </summary>
public class AuthService
{
    private readonly UserService _userService;
    private readonly PasswordHasher<User> _passwordHasher;
    private readonly CurrentUserAccessor _currentUser;

    public AuthService(
        UserService userService,
        PasswordHasher<User> passwordHasher,
        CurrentUserAccessor currentUser)
    {
        _userService = userService;
        _passwordHasher = passwordHasher;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Validates credentials against the database. Does not mutate any auth state.
    /// </summary>
    public async Task<(bool Success, string? Message, User? User)> ValidateCredentialsAsync(
        string matricNo,
        string password)
    {
        if (string.IsNullOrWhiteSpace(matricNo) || string.IsNullOrWhiteSpace(password))
        {
            return (false, "Matric number and password are required.", null);
        }

        var user = await _userService.GetUserByMatricNoAsync(matricNo);

        // Uniform error message: do not reveal whether the account exists.
        if (user is null)
        {
            return (false, "Invalid credentials.", null);
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return (false, "Invalid credentials.", null);
        }

        // A successful rehash is returned by VerifyHashedPassword; persist it so the
        // stored hash uses the current algorithm parameters.
        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            await _userService.UpdatePasswordHashAsync(user);
        }

        return (true, null, user);
    }

    /// <summary>
    /// Returns the authenticated user for the current request, or null when anonymous.
    /// </summary>
    public async Task<User?> GetCurrentUserAsync()
    {
        if (!_currentUser.IsAuthenticated)
        {
            return null;
        }

        return await _currentUser.GetUserAsync();
    }

    public bool IsAuthenticated() => _currentUser.IsAuthenticated;

    public string? CurrentMatricNo() => _currentUser.MatricNo;

    public Role? CurrentRole() => _currentUser.Role;

    public async Task<List<User>> GetAllUsersAsync()
    {
        return await _userService.GetUsersAsync();
    }
}