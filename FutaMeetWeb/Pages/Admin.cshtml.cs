using FutaMeetWeb.Models;
using FutaMeetWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FutaMeetWeb.Pages
{
    public class AdminModel : PageModel
    {
        private readonly SessionService _sessionService;
        public AdminModel(SessionService sessionService)
        {
            _sessionService = sessionService;
        }
        public List<User> UsersInActiveSessions { get; set; }
        public List<Session> ActiveSessions { get; set; }
        public List<User> AppUsers { get; set; }
        public void OnGet()
        {
            ActiveSessions = _sessionService.GetActiveSessions();
            AppUsers = MockApiService.GetUsers();
            int AllLecturers = MockApiService.GetLecturers().Count();

            var partiipantIds = ActiveSessions
                .SelectMany(session => session.ParticipantIds)
                .Distinct()
                .ToList();
            UsersInActiveSessions = AppUsers
                .Where(user => partiipantIds.Contains(user.MatricNo))
                .ToList();
        }
    }
}
