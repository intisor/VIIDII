using FutaMeetWeb.Services;
using FutaMeetWeb.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FutaMeetWeb.Pages
{
    public class JoinSessionModel : PageModel
    {
        private readonly SessionService _sessionService;

        public JoinSessionModel(SessionService sessionService)
        {
            _sessionService = sessionService;
        }

        [BindProperty]
        public string SessionId { get; set; }
        public string CurrentSessionId { get; set; }
        public Session Session { get; set; }
        public string Message { get; set; }
      

        public List<SelectListItem> AvailableSessions { get; set; } = [];
        public bool IsSessionStarted { get; set; }
        public bool IsSessionLecturer { get; set; }

        public IActionResult OnGet(string sessionId)
        {
            LoadAvailableSessions();
            var matricNo = HttpContext.Session.GetString("MatricNo");
            if (string.IsNullOrEmpty(matricNo))
            {
                return RedirectToPage("/Login");
            }

            if (!string.IsNullOrEmpty(sessionId))
            {
                Session = _sessionService.GetSessionById(sessionId);
                if (Session == null)
                {
                    Message = "Session not found.";
                    return Page();
                }
                CurrentSessionId = sessionId;
                IsSessionStarted = Session.IsSessionStarted;
                IsSessionLecturer = Session.LecturerId == matricNo;
                Message = $"Joined session {sessionId}";
                HttpContext.Session.SetString("CurrentSessionId", CurrentSessionId);
            }
            else
            {
                CurrentSessionId = HttpContext.Session.GetString("CurrentSessionId");
                if (!string.IsNullOrEmpty(CurrentSessionId))
                {
                    Session = _sessionService.GetSessionById(CurrentSessionId);
                    if (Session != null)
                    {
                        IsSessionStarted = Session.IsSessionStarted;
                        IsSessionLecturer = Session.LecturerId == matricNo;
                        Message = HttpContext.Session.GetString("SessionMessage");
                    }
                }
            }

            return Page();
        }

        public IActionResult OnPost()
        {
            var matricNo = HttpContext.Session.GetString("MatricNo");
            if (string.IsNullOrEmpty(matricNo))
            {
                return RedirectToPage("/Login");
            }

            if (string.IsNullOrEmpty(SessionId))
            {
                Message = "Session ID is required.";
                return Page();
            }

            Session = _sessionService.GetSessionById(SessionId);
            if (Session == null)
            {
                Message = "Session not found.";
                return Page();
            }

            CurrentSessionId = SessionId;
            IsSessionStarted = Session.IsSessionStarted;
            IsSessionLecturer = Session.LecturerId == matricNo;
            Message = $"Joined session {SessionId}";
            HttpContext.Session.SetString("CurrentSessionId", CurrentSessionId);
            HttpContext.Session.SetString("SessionMessage", Message);
            return RedirectToPage();
        }
        private void LoadAvailableSessions()
        {
            var matricNo = HttpContext.Session.GetString("MatricNo");
            var user = MockApiService.GetUsers().FirstOrDefault(s => s.MatricNo == matricNo);
            AvailableSessions = [.. _sessionService.GetActiveSessions()
                .Where(s => 
                            (user.Department.HasValue && user.Level.HasValue) && 
                            (
                                (s.AllowedDepartments.Contains(user.Department.Value) || s.AllowedDepartments.Contains(Models.User.Departments.Any)) && 
                                (s.AllowedLevels.Contains(user.Level.Value) || s.AllowedLevels.Contains(Models.User.Levels.Any))
                            )
                       )
                .Select(s => new SelectListItem
                {
                    Value = s.SessionId,
                    Text = $"{s.Title} (by {s.LecturerId})"
                })];
   
            if (AvailableSessions.Count == 0)
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
}