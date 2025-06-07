using FutaMeetWeb.Models;
using FutaMeetWeb.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

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

        if (user == null || _passwordHasher.VerifyHashedPassword(user,user.Password,Password) == PasswordVerificationResult.Failed)
        {
            Message = "Invalid Matric No. or Password!";
            return Page();
        }

        HttpContext.Session.SetString("MatricNo", matricNo);   
        Message = $"Logged in as {UserName}";
        return Page();
    }

    public IActionResult OnPostLogout()
    {
        HttpContext.Session.Clear();
        Message = "Logged out!";
        return RedirectToPage("/Index");
    }
}