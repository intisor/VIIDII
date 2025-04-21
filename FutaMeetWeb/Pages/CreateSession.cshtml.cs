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
        public string CurrentSessionId { get; set; }
        public bool IsSessionStarted { get; set; }
        public string Message { get; set; }
        public Session Session { get; set; }
        public bool IsSessionLecturer { get; set; } // Add this property

        public void OnGet()
        {
            var lecturerId = HttpContext.Session.GetString("MatricNo");
            Session = _sessionService.GetSessionsByLecturer(lecturerId).FirstOrDefault();
            if (Session is not null)
            {
                CurrentSessionId = Session.SessionId;
                IsSessionStarted = Session.IsSessionStarted;
                Message = HttpContext.Session.GetString("SessionMessage");
                IsSessionLecturer = Session.LecturerId == lecturerId;
            }
        }
        public IActionResult OnPost(bool? replaceExisting = null)
        {
            var lecturerId = HttpContext.Session.GetString("MatricNo");
            var session = _sessionService.CreateSession(lecturerId, Title, true);
            if (replaceExisting == null && session.Status == SessionStatus.Active && session.Title != Title)
            {
                ShowReplacePrompt = true;
                ExistingSessionId = session.SessionId;
                return Page();
            }
            CurrentSessionId = _sessionService.GetSessionsByLecturer(lecturerId).FirstOrDefault().SessionId;
            Message = replaceExisting == false
                ? $"Created session Kept existing session: {session.SessionId}"
                : $"session: {session.SessionId}";
            IsSessionStarted = session.IsSessionStarted;
            IsSessionLecturer = session.LecturerId == lecturerId;
            HttpContext.Session.SetString("SessionMessage", Message);
            HttpContext.Session.SetString("CurrentSessionId", CurrentSessionId);
            return Page();
        }
        public IActionResult OnPostStartSession()
        {
            var lecturerId = HttpContext.Session.GetString("MatricNo");
            Console.WriteLine($"[DEBUG] OnPostStartSession: lecturerId={lecturerId}");
            if (string.IsNullOrEmpty(lecturerId))
            {
                Console.WriteLine("[DEBUG] OnPostStartSession: MatricNo is null, redirecting to login");
                return RedirectToPage("/Login");
            }

            Session = _sessionService.GetSessionsByLecturer(lecturerId).FirstOrDefault();
            if (Session is null)
            {
                Console.WriteLine("[DEBUG] OnPostStartSession: No session found");
                Message = "No session found to start.";
                return Page();
            }

            Console.WriteLine($"[DEBUG] OnPostStartSession: Starting sessionId={Session.SessionId}");
            _sessionService.StartSession(Session.SessionId);
            CurrentSessionId = Session.SessionId;
            IsSessionStarted = Session.IsSessionStarted;
            IsSessionLecturer = Session.LecturerId == lecturerId;
            Message = $"Session {Session.SessionId} started at {Session.StartTime:HH:mm}";
            HttpContext.Session.SetString("SessionMessage", Message);
            Console.WriteLine($"[DEBUG] OnPostStartSession: Session started, CurrentSessionId={CurrentSessionId}, IsSessionStarted={IsSessionStarted}");
            return Page();
        }
        public IActionResult OnPostEndSession()
        {
            var lecturerId = HttpContext.Session.GetString("MatricNo");
            var sessionId = HttpContext.Session.GetString("CurrentSessionId");
            Session = _sessionService.GetSessionsByLecturer(lecturerId).FirstOrDefault() 
                ?? _sessionService.GetSessionById(sessionId);
            if (Session is null)
            {
                Message = "No session found to stop.";
                return Page();
            }
            _sessionService.EndSession(Session.SessionId, Session.LecturerId);
            CurrentSessionId = Session.SessionId;
            IsSessionStarted = Session.IsSessionStarted;
            IsSessionLecturer = Session.LecturerId == lecturerId;
            Message = $"Session {Session.SessionId} stopped at {Session.EndTime:HH:mm}";
            HttpContext.Session.SetString("SessionMessage", Message);
            return Page();
        }
    }
}