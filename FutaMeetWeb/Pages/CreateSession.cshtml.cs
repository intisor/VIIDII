using FutaMeetWeb.Models;
using FutaMeetWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;

namespace FutaMeetWeb.Pages
{
    public class CreateSessionModel : PageModel
    {
        private readonly SessionService _sessionService;
        public CreateSessionModel(SessionService sessionService)
        {
            _sessionService = sessionService;
        }
        [BindProperty]
        public string Title { get; set; } = string.Empty;
        public bool ShowReplacePrompt { get; set; }
        public string ExistingSessionId { get; set; }
        public string CurrentSessionId { get; set;}
        public string Message { get; set; }
        public void OnGet()
        {
        }
        public IActionResult OnPost(bool? replaceExisting = null)
        {
            var lecturerId = HttpContext.Session.GetString("MatricNo");
            var session = _sessionService.CreateSession(lecturerId,Title, true);
            if (replaceExisting == null && session.Status == SessionStatus.Active && session.Title != Title)
            {
                ShowReplacePrompt = true;
                ExistingSessionId = session.SessionId;
                return Page();
            }
            CurrentSessionId = _sessionService.GetSessionsByLecturer(lecturerId).FirstOrDefault().SessionId;
            Message = replaceExisting == false
                ? $"Created session Kept existing session: {session.SessionId}"
                : $"Created session: {session.SessionId}";
            return Page();
        }
    }
}
