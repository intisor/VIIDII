using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VIIDII.Services
{
    public enum MessageType { File, Text }

    public class Reaction
    {
        public required string UserId { get; set; }
        public required string Emoji { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class Message
    {
        public required string id { get; set; }
        public required string sessionId { get; set; }
        public required string userId { get; set; }
        public required string UserName { get; set; }
        public required string content { get; set; }
        public string? parentId { get; set; }
        public bool isLecturerPost { get; set; }
        public bool isComment { get; set; }
        public MessageType messageType { get; set; } = MessageType.Text;
        public DateTime createdAt { get; set; } = DateTime.UtcNow;
        public List<Reaction> Reactions { get; set; } = new();

        // File Sharing & Q&A (Ephemeral in-memory metadata / P2P transferred)
        public string? fileName { get; set; }
        public string? fileUrl { get; set; }
        public long? fileSizeBytes { get; set; }
        public string? fileSizeFormatted { get; set; }
        public bool isQuestion { get; set; }
        public bool isAnswered { get; set; }

        public int ThumbsUpCount => Reactions.Count(r => r.Emoji == "👍" || r.Emoji == "like" || r.Emoji == "??");
        public bool HasUserReacted(string userId) => Reactions.Any(r => r.UserId == userId);
    }

    /// <summary>
    /// Ephemeral in-memory message coordinator for live sessions.
    /// Zero server storage: chat, questions, and reactions are held strictly in memory
    /// during active sessions and purged when the session ends.
    /// </summary>
    public class MessageService
    {
        private static readonly ConcurrentDictionary<string, List<Message>> _sessionMessages = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new();
        private readonly TimeProvider _clock;

        public MessageService(TimeProvider? clock = null)
        {
            _clock = clock ?? TimeProvider.System;
        }

        public Task<Message> CreatePostAsync(
            string sessionId,
            string userId,
            string userName,
            string content,
            bool isLecturer,
            bool isFile = false,
            string? fileUrl = null,
            string? fileName = null,
            long? fileSizeBytes = null,
            bool isQuestion = false)
        {
            var msg = new Message
            {
                id = Guid.NewGuid().ToString("N"),
                sessionId = sessionId,
                userId = userId,
                UserName = userName,
                content = content,
                isLecturerPost = isLecturer,
                isComment = false,
                messageType = isFile ? MessageType.File : MessageType.Text,
                createdAt = _clock.GetUtcNow().UtcDateTime,
                fileName = fileName,
                fileUrl = fileUrl,
                fileSizeBytes = fileSizeBytes,
                fileSizeFormatted = fileSizeBytes.HasValue ? FormatFileSize(fileSizeBytes.Value) : null,
                isQuestion = isQuestion,
                isAnswered = false
            };
            msg.parentId = msg.id;

            lock (_lock)
            {
                var list = _sessionMessages.GetOrAdd(sessionId, _ => new List<Message>());
                list.Add(msg);
            }

            return Task.FromResult(msg);
        }

        public Task<Message> CreateCommentAsync(string sessionId, string userId, string userName, string content, string postId, bool isLecturer)
        {
            var comment = new Message
            {
                id = Guid.NewGuid().ToString("N"),
                sessionId = sessionId,
                userId = userId,
                UserName = userName,
                content = content,
                parentId = postId,
                isLecturerPost = isLecturer,
                isComment = true,
                messageType = MessageType.Text,
                createdAt = _clock.GetUtcNow().UtcDateTime
            };

            lock (_lock)
            {
                var list = _sessionMessages.GetOrAdd(sessionId, _ => new List<Message>());
                list.Add(comment);
            }

            return Task.FromResult(comment);
        }

        public Task<List<Message>> GetAllMessagesAsync(string sessionId)
        {
            lock (_lock)
            {
                if (_sessionMessages.TryGetValue(sessionId, out var list))
                {
                    return Task.FromResult(list.OrderBy(m => m.createdAt).ToList());
                }
                return Task.FromResult(new List<Message>());
            }
        }

        public Task<bool> AddReactionAsync(string sessionId, string messageId, string userId, string emoji)
        {
            lock (_lock)
            {
                if (_sessionMessages.TryGetValue(sessionId, out var list))
                {
                    var msg = list.FirstOrDefault(m => m.id == messageId);
                    if (msg != null && !msg.Reactions.Any(r => r.UserId == userId && r.Emoji == emoji))
                    {
                        msg.Reactions.Add(new Reaction
                        {
                            UserId = userId,
                            Emoji = emoji,
                            Timestamp = _clock.GetUtcNow().UtcDateTime
                        });
                        return Task.FromResult(true);
                    }
                }
                return Task.FromResult(false);
            }
        }

        public Task<bool> RemoveReactionAsync(string sessionId, string messageId, string userId, string emoji)
        {
            lock (_lock)
            {
                if (_sessionMessages.TryGetValue(sessionId, out var list))
                {
                    var msg = list.FirstOrDefault(m => m.id == messageId);
                    if (msg != null)
                    {
                        var reaction = msg.Reactions.FirstOrDefault(r => r.UserId == userId && r.Emoji == emoji);
                        if (reaction != null)
                        {
                            msg.Reactions.Remove(reaction);
                            return Task.FromResult(true);
                        }
                    }
                }
                return Task.FromResult(false);
            }
        }

        public Task<bool> MarkQuestionAnsweredAsync(string sessionId, string messageId, bool isAnswered)
        {
            lock (_lock)
            {
                if (_sessionMessages.TryGetValue(sessionId, out var list))
                {
                    var msg = list.FirstOrDefault(m => m.id == messageId);
                    if (msg != null)
                    {
                        msg.isAnswered = isAnswered;
                        return Task.FromResult(true);
                    }
                }
                return Task.FromResult(false);
            }
        }

        public static void ClearSessionMessages(string sessionId)
        {
            _sessionMessages.TryRemove(sessionId, out _);
        }

        public static string FormatFileSize(long bytes)
        {
            if (bytes >= 1024 * 1024)
                return $"{(bytes / 1024.0 / 1024.0):F1} MB";
            if (bytes >= 1024)
                return $"{(bytes / 1024.0):F1} KB";
            return $"{bytes} B";
        }
    }
}
