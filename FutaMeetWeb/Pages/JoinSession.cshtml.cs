using FutaMeetWeb.Services;
using FutaMeetWeb.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FutaMeetWeb.Pages;

public class JoinSessionModel : PageModel
{
    private readonly SessionService _sessionService;

    public JoinSessionModel(SessionService sessionService)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
    }

    [BindProperty]
    public string SessionId { get; set; } = string.Empty;

    public string Message { get; set; }

    public List<SelectListItem> AvailableSessions { get; set; } = [];

    public IActionResult OnGet()
    {
        LoadAvailableSessions();
        return Page();
    }

    public IActionResult OnPost()
    {
        var participantId = HttpContext.Session.GetString("MatricNo");
        if (string.IsNullOrEmpty(participantId))
        {
            Message = "Log in first!";
            LoadAvailableSessions();
            return Page();
        }

        var (session, error) = _sessionService.JoinSession(SessionId, participantId);
        if (session == null)
        {
            Message = error ?? "Failed to join session.";
            LoadAvailableSessions();
            return Page();
        }

        Message = $"Joined session: {session.Title} ({session.SessionId})";
        LoadAvailableSessions();
        return Page();
    }

    private void LoadAvailableSessions()
    {
        AvailableSessions = _sessionService.GetSessionsBy(true, s => s.Status == SessionStatus.Active)
            .Select(s => new SelectListItem
            {
                Value = s.SessionId,
                Text = $"{s.Title} (by {s.LecturerId})"
            })
            .ToList();

        if (!AvailableSessions.Any())
        {
            AvailableSessions.Add(new SelectListItem
            {
                Value = "",
                Text = "No active sessions available",
                Disabled = true
            });
        }
    }
}