using FutaMeetWeb.Models;
using FutaMeetWeb.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using static FutaMeetWeb.Models.User;

namespace FutaMeetWeb.Pages;

public class LoginModel : PageModel
{
    private readonly SessionService _sessionService;
    private readonly PasswordHasher<User> _passwordHasher;
    public LoginModel(SessionService sessionService,PasswordHasher<User> passwordHasher)
    {
        _sessionService = sessionService;
        _passwordHasher = passwordHasher;
    }

    [BindProperty]
    public string MatricNo { get; set; }

    [BindProperty]
    public string Password { get; set; }

    public string Message { get; set; }
    public string UserName { get; set; }

    public bool IsLoggedIn => !string.IsNullOrEmpty(HttpContext.Session.GetString("MatricNo"));
    public string Role { get; set; }
    public Departments? Department { get; set; }
    public Levels? Level { get; set; }

    public IEnumerable<SelectListItem> UserOptions { get; set; }


    public IActionResult OnGet()
    {
        UserOptions = MockApiService.GetUsers()
            .Select(u => new SelectListItem
            {
                Value = u.MatricNo,
                Text = $"{u.Name} ({u.Role})"
            })
            .ToList();
        var matricNo = HttpContext.Session.GetString("MatricNo");
        if (!string.IsNullOrEmpty(matricNo))
        {
            var user = MockApiService.GetUsers().FirstOrDefault(u => u.MatricNo == matricNo);
            Role = user.Role.ToString() ?? "";
            UserName = user.Name ?? "";
            Department = user.Department;
            Level = user.Level;

        }
        return Page(); // Explicit render
    }   

    public IActionResult OnPost(string matricNo)
    {
        if (string.IsNullOrEmpty(MatricNo))
        {
            Message = "Pick a user!";
            return Page();
        }
        UserOptions = [.. MockApiService.GetUsers()
           .Select(u => new SelectListItem
           {
               Value = u.MatricNo,
               Text = $"{u.Name} ({u.Role})"
           })];
        var user = MockApiService.GetUsers().FirstOrDefault(u => u.MatricNo == MatricNo);
        Role = user.Role.ToString() ?? "";
        UserName = user?.Name ?? "";
        Department = user.Department;
        Level = user.Level;

        if (user == null)
        {
            Message = "Invalid Matric No.!";
            return Page();
        }

        // Check if password is correct first
        if (_passwordHasher.VerifyHashedPassword(user, user.Password, Password) == PasswordVerificationResult.Failed)
        {
            Message = "Invalid Password!";
            return Page();
        }

        if (MockApiService.IsUserLoggedIn(MatricNo))
        {
            Message = $"User {user.Name} is already logged in from another session. Please try again later or contact admin.";
            return Page();
        }
        if (!MockApiService.TryLoginUser(MatricNo))
        {
            Message = "Unable to login. User may already be logged in.";
            return Page();
        }

        HttpContext.Session.SetString("MatricNo", matricNo);   
        Message = $"Logged in as {UserName}";
        return Page();
    }

    public IActionResult OnPostLogout()
    {
        var matricNo = HttpContext.Session.GetString("MatricNo");
        if (!string.IsNullOrEmpty(matricNo))
        {
            MockApiService.LogoutUser(matricNo);
        }
        HttpContext.Session.Clear();
        Message = "Logged out!";
        return RedirectToPage("/Index");
    }
}