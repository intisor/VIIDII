using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Collections.Generic;

namespace VIIDII.Services
{
    public enum MessageType { File, Text }

    public class Reaction
    {
        public required string UserId { get; set; }
        public required string Emoji { get; set; } // For now: just "??"
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class Message
    {
        public string id { get; set; } = Guid.CreateVersion7().ToString();
        public required string sessionId { get; set; }
        public required string userId { get; set; }
        public required string UserName { get; set; }
        public required string content { get; set; }
        public string? parentId { get; set; }
        public bool isLecturerPost { get; set; }
        public bool isComment { get; set; }
        public MessageType messageType { get; set; } = MessageType.Text;
        public DateTime createdAt { get; set; } = DateTime.UtcNow.AddHours(1);
        public List<Reaction> Reactions { get; set; } = new();

        // Helper properties
        public int ThumbsUpCount => Reactions.Count(r => r.Emoji == "??");
        public bool HasUserReacted(string userId) => Reactions.Any(r => r.UserId == userId);
    }

    public class MessageService
    {
        private readonly ConcurrentBag<Message> _messages = new ConcurrentBag<Message>();

        public Message CreatePost(string sessionId, string userId, string userName, string content, bool isLecturer, bool isFile = false)
        {
            if (!isLecturer)
            {
                throw new InvalidOperationException("Only lecturers can create posts.");
            }

            var message = new Message
            {
                sessionId = sessionId,
                userId = userId,
                UserName = userName,
                content = content,
                isLecturerPost = true,
                isComment = false,
                messageType = isFile ? MessageType.File : MessageType.Text
            };
            message.parentId = message.id; // Set ParentId to itself for posts
            _messages.Add(message);
            return message;
        }

        public Message CreateComment(string sessionId, string userId, string userName, string content, string postId, bool isLecturer)
        {
            var post = _messages.FirstOrDefault(m => m.id == postId && m.sessionId == sessionId);
            if (post == null)
            {
                throw new InvalidOperationException("Post not found.");
            }
            if (post.parentId != post.id || !post.isLecturerPost)
            {
                throw new InvalidOperationException("Can only reply to lecturer posts.");
            }

            var message = new Message
            {
                sessionId = sessionId,
                userId = userId,
                UserName = userName,
                content = content,
                parentId = postId,
                isLecturerPost = isLecturer, // This indicates if the *commenter* is a lecturer
                isComment = true,
            };

            _messages.Add(message);
            return message;
        }

        public List<Message> GetAllMessages(string sessionId)
        {
            return _messages
                .Where(m => m.sessionId == sessionId)
                .OrderBy(m => m.createdAt)
                .ToList();
        }

        public bool AddReaction(string sessionId, string messageId, string userId, string emoji)
        {
            var message = _messages.FirstOrDefault(m => m.id == messageId && m.sessionId == sessionId);
            if (message == null) return false;

            // Check if user already reacted with this emoji
            if (message.Reactions.Any(r => r.UserId == userId && r.Emoji == emoji))
            {
                return false; // Already reacted
            }

            message.Reactions.Add(new Reaction
            {
                UserId = userId,
                Emoji = emoji,
                Timestamp = DateTime.UtcNow
            });

            return true;
        }

        public bool RemoveReaction(string sessionId, string messageId, string userId, string emoji)
        {
            var message = _messages.FirstOrDefault(m => m.id == messageId && m.sessionId == sessionId);
            if (message == null) return false;

            var reaction = message.Reactions.FirstOrDefault(r => r.UserId == userId && r.Emoji == emoji);
            if (reaction == null) return false;

            message.Reactions.Remove(reaction);
            return true;
        }
    }
}