using FutaMeetWeb.Models;
using FutaMeetWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using FutaMeetWeb.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FutaMeetWeb.Pages
{
    public class CreateSessionModel : PageModel
    {
        private readonly SessionService _sessionService;
        private readonly IHubContext<SessionHub> _hubContext;

        public CreateSessionModel(SessionService sessionService, IHubContext<SessionHub> hubContext)
        {
            _sessionService = sessionService;
            _hubContext = hubContext;
        }

        [BindProperty]
        public string Title { get; set; }
        public bool ShowReplacePrompt { get; set; }
        public string ExistingSessionId { get; set; }
        public string CurrentSessionId { get; set; }
        public bool IsSessionStarted { get; set; }
        public string Message { get; set; }
        public Session Session { get; set; }
        public string LecturerName { get; set; }
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

        public IActionResult OnPost(bool replaceExisting = false)
        {
            var lecturerId = HttpContext.Session.GetString("MatricNo");
            if (AllowedDepartments == null || !AllowedDepartments.Any())
            {
                AllowedDepartments.Add(Models.User.Departments.Any);
            }

            if (AllowedLevels == null || !AllowedLevels.Any())
            {
                AllowedLevels.Add(Models.User.Levels.Any);
            }

            Session = _sessionService.CreateSession(lecturerId, Title, AllowedDepartments, AllowedLevels, replaceExisting);
            if (!replaceExisting && Session.Status == SessionStatus.Started)
            {
                ShowReplacePrompt = true;
                ExistingSessionId = Session.SessionId;
                Message = $"Created session. Kept existing session: {Session.SessionId}";
                CurrentSessionId = _sessionService.GetSessionsByLecturer(lecturerId).FirstOrDefault().SessionId;
                return Page();
            }
            CurrentSessionId = Session.SessionId;
            LecturerName = MockApiService.GetLecturers().Where(s => s.MatricNo == lecturerId).FirstOrDefault().Name;
            Message =  $"session: {Session.SessionId}";
            IsSessionStarted = Session.IsSessionStarted;
            IsSessionLecturer = Session.LecturerId == lecturerId; // No conflict now
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

        public  IActionResult OnPostEndSession()
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
            // Notify all clients in the session via SignalR
            _hubContext.Clients.Group(Session.SessionId).SendAsync("SessionEnded", Session.SessionId);
            CurrentSessionId = Session.SessionId;
            IsSessionStarted = Session.IsSessionStarted;
            IsSessionLecturer = Session.LecturerId == lecturerId; // No conflict now
            Message = $"Session {Session.SessionId} stopped at {Session.EndTime:HH:mm}";
            HttpContext.Session.SetString("SessionMessage", Message);
            return RedirectToPage("/SessionRecap", new { sessionId });
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
}