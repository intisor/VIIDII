using FutaMeetWeb.Models;
using FutaMeetWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FutaMeetWeb.Pages
{
    public class SessionRecapModel : PageModel
    {
        private readonly SessionService _sessionService;
        private readonly MessageService _messageService;

        public SessionRecapModel(SessionService sessionService, MessageService messageService)
        {
            _sessionService = sessionService;
            _messageService = messageService;
        }

        public Session Session { get; set; }
        public Dictionary<string, double> Scores { get; set; }
        public List<(DateTime Timestamp, string Description)> TimelineEvents { get; set; }

        public IActionResult OnGet(string sessionId)
        {
            Session = _sessionService.GetSessionById(sessionId);
            if (Session == null || Session.Status != SessionStatus.Ended)
                return RedirectToPage("/Index");

            Scores = _sessionService.CalculateAttendanceScore(sessionId);
            TimelineEvents = new List<(DateTime, string)>();

            foreach (var (participantId, events) in Session.ParticipantEvents)
            {
                foreach (var (status, time) in events)
                {
                    TimelineEvents.Add((time, $"{participantId} changed to {status}"));
                }
            }

            var messages = _messageService.GetAllMessages(sessionId);
            foreach (var msg in messages)
            {
                var desc = msg.isLecturerPost ? $"{msg.userId} posted: {msg.content}" : $"{msg.userId} commented: {msg.content}";
                TimelineEvents.Add((msg.createdAt, desc));
            }

            TimelineEvents = TimelineEvents.OrderBy(e => e.Timestamp).ToList();
            return Page();
        }
    }
}