using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Collections.Generic;

namespace FutaMeetWeb.Services
{
    public class Message
    {
        public string id { get; set; } = Guid.CreateVersion7().ToString();
        public string sessionId { get; set; }
        public string userId { get; set; }
        public string content { get; set; }
        public string parentId { get; set; }
        public bool isLecturerPost { get; set; }
        public bool isComment { get; set; } // Unused but kept for future-proofing
        public DateTime createdAt { get; set; } = DateTime.UtcNow;
    }

    public class MessageService
    {
        private readonly ConcurrentBag<Message> _messages = new ConcurrentBag<Message>();

        public Message CreatePost(string sessionId, string userId, string content, bool isLecturer)
        {
            if (!isLecturer)
            {
                throw new InvalidOperationException("Only lecturers can create posts.");
            }

            var message = new Message
            {
                sessionId = sessionId,
                userId = userId,
                content = content,
                isLecturerPost = true,
                isComment = false,
            };
            message.parentId = message.id; // Set ParentId to itself for posts
            _messages.Add(message);
            return message;
        }

        public Message CreateComment(string sessionId, string userId, string content, string postId, bool isLecturer)
        {
            var post = _messages.FirstOrDefault(m => m.id == postId && m.sessionId == sessionId);
            if (post == null)
            {
                throw new InvalidOperationException("Post not found.");
            }
            if (post.parentId != post.id || !post.isLecturerPost) // Fix: ParentId, IsLecturerPost, check ParentId == Id
            {
                throw new InvalidOperationException("Can only reply to lecturer posts.");
            }

            var message = new Message
            {
                sessionId = sessionId,
                userId = userId,
                content = content,
                parentId = postId,
                isLecturerPost = isLecturer,
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
    }
}