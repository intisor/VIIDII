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
        public Dictionary<string, SessionService.ParticipantScoreDetails> Scores { get; set; }
        public List<(DateTime Timestamp, string Description)> TimelineEvents { get; set; }
        public double AverageAttendanceScore { get; set; }
        public string MostCommonPenaltyReason { get; set; }
        public int ParticipantsAbove70Percent { get; set; }

        public IActionResult OnGet(string sessionId)
        {
            Session = _sessionService.GetSessionById(sessionId);
            if (Session == null || Session.Status != SessionStatus.Ended)
                return RedirectToPage("/Index");

            Scores = _sessionService.CalculateAttendanceScore(sessionId);
            TimelineEvents = new List<(DateTime, string)>();

            if (Scores != null && Scores.Any())
            {
                AverageAttendanceScore = Scores.Values.Average(s => s.FinalScorePercentage);
                ParticipantsAbove70Percent = Scores.Values.Count(s => s.FinalScorePercentage >= 70);

                // Determine most common penalty
                var penaltyCounts = new Dictionary<string, int>();
                foreach (var scoreDetail in Scores.Values)
                {
                    if (scoreDetail.TimeInactiveMinutes > 0) penaltyCounts["Inactive"] = penaltyCounts.GetValueOrDefault("Inactive", 0) + 1;
                    if (scoreDetail.TimeBatteryLowMinutes > 0) penaltyCounts["Battery Low"] = penaltyCounts.GetValueOrDefault("Battery Low", 0) + 1;
                    if (scoreDetail.TimeDataFinishedMinutes > 0) penaltyCounts["Data Finished"] = penaltyCounts.GetValueOrDefault("Data Finished", 0) + 1;
                    if (scoreDetail.TimeDisconnectedMinutes > 0) penaltyCounts["Disconnected"] = penaltyCounts.GetValueOrDefault("Disconnected", 0) + 1;
                }
                MostCommonPenaltyReason = penaltyCounts.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key ?? "N/A";
            }
            else
            {
                AverageAttendanceScore = 0;
                ParticipantsAbove70Percent = 0;
                MostCommonPenaltyReason = "N/A";
            }

            //foreach (var (participantId, events) in Session.ParticipantEvents)
            //{
            //    foreach (var (status, time) in events)
            //    {
            //        TimelineEvents.Add((time, $"{participantId} changed to {status}"));
            //    }
            //}

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