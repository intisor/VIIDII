using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FutaMeetWeb.Pages;

public class LoginModel : PageModel
{
    [BindProperty]
    public string MatricNo { get; set; } = string.Empty;

    public string Message { get; set; }

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

        HttpContext.Session.SetString("MatricNo", matricNo);
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