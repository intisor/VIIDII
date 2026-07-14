using System;
using System.Collections.Generic;
using System.Linq;
using VIIDII.Data;
using VIIDII.Models;

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

        public int ThumbsUpCount => Reactions.Count(r => r.Emoji == "👍");
        public bool HasUserReacted(string userId) => Reactions.Any(r => r.UserId == userId);
    }

    public class MessageService
    {
        private readonly MessagePersistenceService _persistenceService;
        private readonly SessionService _sessionService;

        public MessageService(MessagePersistenceService persistenceService, SessionService sessionService)
        {
            _persistenceService = persistenceService;
            _sessionService = sessionService;
        }

        public async Task<Message> CreatePostAsync(string sessionId, string userId, string userName, string content, bool isLecturer, bool isFile = false)
        {
            if (!isLecturer)
            {
                throw new InvalidOperationException("Only lecturers can create posts.");
            }

            var persisted = await _persistenceService.CreateAndPersistPostAsync(sessionId, userId, content);
            if (persisted == null)
            {
                throw new InvalidOperationException("Failed to create post.");
            }

            return await MapMessageAsync(persisted, sessionId, userId, userName, isFile);
        }

        public async Task<Message> CreateCommentAsync(string sessionId, string userId, string userName, string content, string postId, bool isLecturer)
        {
            if (!int.TryParse(postId, out var parentMessageId))
            {
                throw new InvalidOperationException("Post not found.");
            }

            var persisted = await _persistenceService.CreateAndPersistCommentAsync(sessionId, userId, content, parentMessageId);
            if (persisted == null)
            {
                throw new InvalidOperationException("Failed to create comment.");
            }

            return await MapMessageAsync(persisted, sessionId, userId, userName, false, isLecturer);
        }

        public async Task<List<Message>> GetAllMessagesAsync(string sessionId)
        {
            var messages = await _persistenceService.GetSessionPostsAsync(sessionId);
            var result = new List<Message>(messages.Count);

            foreach (var message in messages)
            {
                result.Add(await MapMessageAsync(message));
            }

            return result.OrderBy(m => m.createdAt).ToList();
        }

        public async Task<bool> AddReactionAsync(string sessionId, string messageId, string userId, string emoji)
        {
            if (!int.TryParse(messageId, out var parsedMessageId))
            {
                return false;
            }

            var updatedMessage = await _persistenceService.AddReactionAsync(parsedMessageId, userId, emoji);
            return updatedMessage != null && updatedMessage.Session.SessionId == sessionId;
        }

        public async Task<bool> RemoveReactionAsync(string sessionId, string messageId, string userId, string emoji)
        {
            if (!int.TryParse(messageId, out var parsedMessageId))
            {
                return false;
            }

            var updatedMessage = await _persistenceService.RemoveReactionAsync(parsedMessageId, userId, emoji);
            return updatedMessage != null && updatedMessage.Session.SessionId == sessionId;
        }

        private async Task<Message> MapMessageAsync(Models.Message message, string? sessionId = null, string? userId = null, string? userName = null, bool isFile = false, bool? isLecturerOverride = null)
        {
            ArgumentNullException.ThrowIfNull(message);

            var resolvedSessionId = sessionId ?? message.Session?.SessionId ?? await ResolveSessionIdAsync(message.SessionId);
            var resolvedUserId = userId ?? message.Author?.MatricNo ?? await ResolveUserIdAsync(message.AuthorId);
            var resolvedUserName = userName ?? message.Author?.Name ?? resolvedUserId;
            var reactions = ParseReactions(message.Reaction);
            var isLecturer = isLecturerOverride ?? (message.Author?.Role == Role.Lecturer);

            return new Message
            {
                id = message.Id.ToString(),
                sessionId = resolvedSessionId,
                userId = resolvedUserId,
                UserName = resolvedUserName,
                content = message.Content,
                parentId = message.ParentId?.ToString() ?? message.Id.ToString(),
                isLecturerPost = isLecturer,
                isComment = message.ParentId.HasValue,
                messageType = isFile ? MessageType.File : MessageType.Text,
                createdAt = message.CreatedAt,
                Reactions = reactions
            };
        }

        private static List<Reaction> ParseReactions(string? reactionData)
        {
            if (string.IsNullOrWhiteSpace(reactionData))
            {
                return new List<Reaction>();
            }

            return reactionData
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(entry => entry.Split(':', 2, StringSplitOptions.TrimEntries))
                .Where(parts => parts.Length == 2)
                .Select(parts => new Reaction
                {
                    UserId = parts[0],
                    Emoji = parts[1],
                    Timestamp = DateTime.UtcNow
                })
                .ToList();
        }

        private async Task<string> ResolveSessionIdAsync(int sessionDbId)
        {
            var session = await _sessionService.GetSessionByDbIdAsync(sessionDbId);
            return session?.SessionId ?? sessionDbId.ToString();
        }

        private async Task<string> ResolveUserIdAsync(int authorId)
        {
            var user = await _persistenceService.GetUserByIdAsync(authorId);
            return user?.MatricNo ?? authorId.ToString();
        }
    }
}
