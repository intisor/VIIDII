using FutaMeetWeb.Models;
using FutaMeetWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

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
        public string Title { get; set; }
        public bool ShowReplacePrompt { get; set; }
        public string ExistingSessionId { get; set; }
        public string CurrentSessionId { get; set; }
        public bool IsSessionStarted { get; set; }
        public string Message { get; set; }
        public Session Session { get; set; }
        public bool IsSessionLecturer { get; set; } // Fixed: Changed from method group to property

        [BindProperty]
        public List<User.Departments> AllowedDepartments { get; set; } = new();

        [BindProperty]
        public List<User.Levels> AllowedLevels { get; set; } = new();

        public IEnumerable<SelectListItem> DepartmentOptions => Enum.GetValues<User.Departments>()
            .Cast<User.Departments>()
            .Select(d => new SelectListItem { Value = d.ToString(), Text = d.ToString() });

        public IEnumerable<SelectListItem> LevelOptions => Enum.GetValues<User.Levels>()
            .Cast<User.Levels>()
            .Select(l => new SelectListItem { Value = l.ToString(), Text = l.ToString() });


        private bool IsSessionLecturerMethod(string sessionId, string matricNo) // Renamed method to avoid conflict
        {
            var session = _sessionService.GetSessionById(sessionId);
            return session != null && session.LecturerId == matricNo;
        }
        public void OnGet()
        {
            var lecturerId = HttpContext.Session.GetString("MatricNo");
            Session = _sessionService.GetSessionsByLecturer(lecturerId).FirstOrDefault();
            if (Session is not null)
            {
                CurrentSessionId = Session.SessionId;
                IsSessionStarted = Session.IsSessionStarted;
                Message = HttpContext.Session.GetString("SessionMessage");
                IsSessionLecturer = Session.LecturerId == lecturerId; // No conflict now
            }
        }

        public IActionResult OnPost(bool? replaceExisting = null)
        {
            var lecturerId = HttpContext.Session.GetString("MatricNo");
            var session = _sessionService.CreateSession(lecturerId, Title, AllowedDepartments, AllowedLevels, true);
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
            IsSessionLecturer = session.LecturerId == lecturerId; // No conflict now
            HttpContext.Session.SetString("SessionMessage", Message);
            HttpContext.Session.SetString("CurrentSessionId", CurrentSessionId);
            return Page();
        }

        public IActionResult OnPostStartSession()
        {
            var lecturerId = HttpContext.Session.GetString("MatricNo");
            if (string.IsNullOrEmpty(lecturerId))
            {
                return RedirectToPage("/Login");
            }

            Session = _sessionService.GetSessionsByLecturer(lecturerId).FirstOrDefault();
            if (Session is null)
            {
                Message = "No session found to start.";
                return Page();
            }

            _sessionService.StartSession(Session.SessionId);
            CurrentSessionId = Session.SessionId;
            IsSessionStarted = Session.IsSessionStarted;
            IsSessionLecturer = Session.LecturerId == lecturerId; // No conflict now
            Message = $"Session {Session.SessionId} started at {Session.StartTime:HH:mm}";

            Console.WriteLine($"Session started: {Session.SessionId}, IsSessionStarted: {IsSessionStarted}");

            HttpContext.Session.SetString("SessionMessage", Message);
            return RedirectToPage("/CreateSession", new { sessionId = CurrentSessionId });
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
            IsSessionLecturer = Session.LecturerId == lecturerId; // No conflict now
            Message = $"Session {Session.SessionId} stopped at {Session.EndTime:HH:mm}";
            HttpContext.Session.SetString("SessionMessage", Message);
            return RedirectToPage("/SessionRecap", new { sessionId });
        }
    }
}