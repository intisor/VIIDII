using FutaMeetWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FutaMeetWeb.Pages;

public class LoginModel : PageModel
{
    private readonly SessionService _sessionService;

    public LoginModel(SessionService sessionService)
    {
        _sessionService = sessionService;
    }

    [BindProperty]
    public string MatricNo { get; set; }

    [BindProperty]
    public string Password { get; set; }

    public string Message { get; set; }
    public bool IsLoggedIn => !string.IsNullOrEmpty(HttpContext.Session.GetString("MatricNo"));


    public IActionResult OnGet()
    {
        return Page(); // Explicit render
    }

    public IActionResult OnPost(string matricNo)
    {
        if (string.IsNullOrEmpty(MatricNo))
        {
            Message = "Pick a user!";
            return Page();
        }
        var user = MockApiService.GetUsers().FirstOrDefault(u => u.MatricNo == MatricNo);

        if (user == null || user.Password != Password)
        {
            Message = "Invalid Matric No. or Password!";
            return Page();
        }

        HttpContext.Session.SetString("MatricNo", matricNo);
        if (!_sessionService.IsLecturer(MatricNo))
        {
            // Not a lecturer, so treat as student and redirect
            return RedirectToPage("/JoinSession");
        }
        Message = $"Logged in as {MatricNo}";
        return Page();
    }

    public IActionResult OnPostLogout()
    {
        HttpContext.Session.Clear();
        Message = "Logged out!";
        return Page();
    }
}