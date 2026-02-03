using System;
using System.Collections.Generic;
using VIIDII.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace VIIDII.Services;

/// <summary>
/// Authentication service for managing user login and session state in Blazor
/// Persists authentication state to browser storage for page refresh support
/// </summary>
public class AuthService
{
    private readonly MockApiService _apiService;
    private readonly PasswordHasher<User> _passwordHasher;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ProtectedLocalStorage _protectedLocalStorage;
    private User? _currentUser;
    private bool _isInitialized = false;
    private const string AUTH_KEY = "viidii_auth_user";

    public AuthService(
        MockApiService apiService, 
        PasswordHasher<User> passwordHasher, 
        IHttpContextAccessor httpContextAccessor,
        ProtectedLocalStorage protectedLocalStorage)
    {
        _apiService = apiService;
        _passwordHasher = passwordHasher;
        _httpContextAccessor = httpContextAccessor;
        _protectedLocalStorage = protectedLocalStorage;
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
            
            var user = MockApiService.GetUsers().FirstOrDefault(u => u.MatricNo == matricNo);

            if (user == null)
            {
                Console.WriteLine($"[AuthService] User not found with MatricNo: {matricNo}");
                return (false, "User not found", null);
            }

            Console.WriteLine($"[AuthService] User found: {user.Name} with role {user.Role}");
            
            var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.Password, password);
            if (verificationResult == PasswordVerificationResult.Failed)
            {
                Console.WriteLine($"[AuthService] Password verification failed for {matricNo}");
                return (false, "Invalid password", null);
            }

            Console.WriteLine($"[AuthService] Password verified successfully");
            
            // Store in-memory for current circuit
            _currentUser = user;
            _isInitialized = true;
            
            // Persist to browser storage (encrypted)
            await _protectedLocalStorage.SetAsync(AUTH_KEY, matricNo);
            Console.WriteLine($"[AuthService] User stored in memory and browser storage for {matricNo}");
            
            return (true, null, user);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthService] Login error: {ex.Message} - {ex.StackTrace}");
            return (false, $"Login error: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Initializes the auth service by attempting to restore user from browser storage
    /// Call this once per circuit initialization (e.g., in App.razor or layout)
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            Console.WriteLine($"[AuthService] Initializing - attempting to restore user from browser storage");
            var result = await _protectedLocalStorage.GetAsync<string>(AUTH_KEY);
            
            if (result.Success && !string.IsNullOrEmpty(result.Value))
            {
                var matricNo = result.Value;
                var user = MockApiService.GetUsers().FirstOrDefault(u => u.MatricNo == matricNo);
                
                if (user != null)
                {
                    _currentUser = user;
                    Console.WriteLine($"[AuthService] User restored from storage: {user.Name} ({matricNo})");
                }
                else
                {
                    Console.WriteLine($"[AuthService] MatricNo found in storage but user no longer exists: {matricNo}");
                    await _protectedLocalStorage.DeleteAsync(AUTH_KEY);
                }
            }
            else
            {
                Console.WriteLine($"[AuthService] No user found in browser storage");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthService] Error initializing from storage: {ex.Message}");
        }
        finally
        {
            _isInitialized = true;
        }
    }

    public async Task<User?> GetCurrentUserAsync()
    {
        // Initialize from storage if not already done
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        Console.WriteLine($"[AuthService.GetCurrentUserAsync] Checking current user. _currentUser is: {(_currentUser?.Name ?? "NULL")}");
        
        if (_currentUser != null)
        {
            Console.WriteLine($"[AuthService.GetCurrentUserAsync] Returning user: {_currentUser.Name}");
            return _currentUser;
        }
        
        Console.WriteLine($"[AuthService.GetCurrentUserAsync] No user found");
        return null;
    }

    public async Task LogoutAsync()
    {
        // Clear in-memory user
        _currentUser = null;
        
        // Clear browser storage
        try
        {
            await _protectedLocalStorage.DeleteAsync(AUTH_KEY);
            Console.WriteLine($"[AuthService] User logged out and cleared from memory and storage");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthService] Error clearing storage on logout: {ex.Message}");
        }
    }

    public bool IsAuthenticated()
    {
        return _currentUser != null;
    }

    public string? GetCurrentMatricNo()
    {
        return _currentUser?.MatricNo;
    }

    public List<User> GetAllUsers()
    {
        return MockApiService.GetUsers();
    }
}
