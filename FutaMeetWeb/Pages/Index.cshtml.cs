using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FutaMeetWeb.Pages;

public class IndexModel : PageModel
{
    public string? Message { get; set; }

    public IActionResult OnGet()
    {
        Message = "Welcome to FutaMeet!";
        return Page();
    }
}