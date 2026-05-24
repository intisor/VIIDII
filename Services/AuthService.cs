using VIIDII.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using System.Text;

namespace VIIDII.Services;

/// <summary>
/// Authentication service for managing user login and session state in Blazor
/// </summary>
public class AuthService
{
    private readonly UserService _userService;
    private readonly PasswordHasher<User> _passwordHasher;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private User? _currentUser;

    public AuthService(UserService userService, PasswordHasher<User> passwordHasher, IHttpContextAccessor httpContextAccessor)
    {
        _userService = userService;
        _passwordHasher = passwordHasher;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<(bool Success, string? Message, User? User)> LoginAsync(string matricNo, string password)
    {
        try
        {
            // Debug logging
            Console.WriteLine($"[AuthService] Login attempt for MatricNo: {matricNo}");
            
            if (string.IsNullOrWhiteSpace(matricNo) || string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine($"[AuthService] Empty credentials provided");
                return (false, "Matric No and Password are required", null);
            }
            
            var user = await _userService.GetUserByMatricNoAsync(matricNo);

            if (user == null)
            {
                Console.WriteLine($"[AuthService] User not found with MatricNo: {matricNo}");
                return (false, "User not found", null);
            }

            Console.WriteLine($"[AuthService] User found: {user.Name} with role {user.Role}");
            
            var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (verificationResult == PasswordVerificationResult.Failed)
            {
                Console.WriteLine($"[AuthService] Password verification failed for {matricNo}");
                return (false, "Invalid password", null);
            }

            Console.WriteLine($"[AuthService] Password verified successfully");
            
            // Store user in memory for Blazor (not in session during interactive render)
            _currentUser = user;
            Console.WriteLine($"[AuthService] User stored in memory for {matricNo}");

            // Set session for SSR and other middleware compatibility
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.Session != null)
            {
                try
                {
                    httpContext.Session.SetString("MatricNo", matricNo);
                    Console.WriteLine($"[AuthService] Session set with MatricNo: {matricNo}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AuthService] Warning: Failed to set session: {ex.Message}. Falling back to in-memory login state.");
                }
            }
            
            // Return user; state is kept in-memory for the Blazor circuit
            return (true, null, user);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthService] Login error: {ex.Message} - {ex.StackTrace}");
            return (false, $"Login error: {ex.Message}", null);
        }
    }

    public async Task<User?> GetCurrentUserAsync()
    {
        Console.WriteLine($"[AuthService.GetCurrentUserAsync] Checking current user. _currentUser is: {(_currentUser?.Name ?? "NULL")}");
        
        // First check in-memory user (for Blazor interactive)
        if (_currentUser != null)
        {
            Console.WriteLine($"[AuthService.GetCurrentUserAsync] Returning stored user: {_currentUser.Name}");
            return _currentUser;
        }

        // If in-memory is null, try to load from HttpContext session (for SSR/enhanced navigation)
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.Session != null)
        {
            var matricNo = httpContext.Session.GetString("MatricNo");
            if (!string.IsNullOrEmpty(matricNo))
            {
                Console.WriteLine($"[AuthService.GetCurrentUserAsync] Found MatricNo in session: {matricNo}. Loading user...");
                _currentUser = await _userService.GetUserByMatricNoAsync(matricNo);
                if (_currentUser != null)
                {
                    return _currentUser;
                }
            }
        }
        
        Console.WriteLine($"[AuthService.GetCurrentUserAsync] No user found, returning null");
        return null;
    }

    public async Task LogoutAsync()
    {
        // Clear in-memory user
        _currentUser = null;
        
        // Clear session if available
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.Session != null)
        {
            try
            {
                httpContext.Session.Clear();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Could not clear session: {ex.Message}");
            }
        }
    }

    public bool IsAuthenticated()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        return httpContext?.Session.GetString("MatricNo") != null;
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        return await _userService.GetUsersAsync();
    }
}
