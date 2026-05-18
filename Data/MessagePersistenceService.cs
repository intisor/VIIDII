using VIIDII.Models;
using VIIDII.Services;
using Microsoft.EntityFrameworkCore;

namespace VIIDII.Data
{
    /// <summary>
    /// Extension service that adds persistence capabilities to MessageService
    /// Bridges in-memory message state with database storage
    /// Works with VIIDII.Models.Message for EF Core persistence
    /// </summary>
    public class MessagePersistenceService
    {
        private readonly MessageRepository _messageRepository;
        private readonly UserService _userService;
        private readonly ViidiiDbContext _context;

        public MessagePersistenceService(MessageRepository messageRepository, UserService userService, ViidiiDbContext context)
        {
            _messageRepository = messageRepository;
            _userService = userService;
            _context = context;
        }

        /// <summary>
        /// Create post and persist to database
        /// </summary>
        public async Task<Models.Message?> CreateAndPersistPostAsync(
            string sessionId,
            string authorMatricNo,
            string content)
        {
            var author = await _userService.GetUserByMatricNoAsync(authorMatricNo);
            if (author == null || author.Role != Role.Lecturer)
                return null;

            var session = await _context.Sessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId);
            if (session == null)
                return null;

            var message = new Models.Message
            {
                SessionId = session.Id,
                AuthorId = author.Id,
                Content = content,
                ParentId = null,
                Reaction = null,
                CreatedAt = DateTime.UtcNow
            };

            return await _messageRepository.CreateMessageAsync(message);
        }

        /// <summary>
        /// Create comment and persist to database
        /// </summary>
        public async Task<Models.Message?> CreateAndPersistCommentAsync(
            string sessionId,
            string authorMatricNo,
            string content,
            int parentMessageId)
        {
            var author = await _userService.GetUserByMatricNoAsync(authorMatricNo);
            if (author == null)
                return null;

            var parentMessage = await _messageRepository.GetMessageByIdAsync(parentMessageId);
            if (parentMessage == null)
                return null;

            var message = new Models.Message
            {
                SessionId = parentMessage.SessionId,
                AuthorId = author.Id,
                Content = content,
                ParentId = parentMessageId,
                Reaction = null,
                CreatedAt = DateTime.UtcNow
            };

            return await _messageRepository.CreateMessageAsync(message);
        }

        /// <summary>
        /// Add reaction to message
        /// </summary>
        public async Task<Models.Message?> AddReactionAsync(int messageId, string authorMatricNo, string reaction)
        {
            var author = await _userService.GetUserByMatricNoAsync(authorMatricNo);
            if (author == null)
                return null;

            var message = await _messageRepository.GetMessageByIdAsync(messageId);
            if (message == null)
                return null;

            // Store reaction as serialized emoji (for now, just store the emoji string)
            if (string.IsNullOrEmpty(message.Reaction))
                message.Reaction = reaction;
            else if (!message.Reaction.Contains(reaction))
                message.Reaction += $",{reaction}";

            return await _messageRepository.UpdateMessageAsync(message);
        }

        /// <summary>
        /// Get all posts for a session
        /// </summary>
        public async Task<List<Models.Message>> GetSessionPostsAsync(string sessionId)
        {
            return await _messageRepository.GetSessionMessagesBySessionIdStringAsync(sessionId);
        }

        /// <summary>
        /// Get replies for a post
        /// </summary>
        public async Task<List<Models.Message>> GetPostRepliesAsync(int postId)
        {
            return await _messageRepository.GetMessageRepliesAsync(postId);
        }

        /// <summary>
        /// Get message by ID
        /// </summary>
        public async Task<Models.Message?> GetMessageByIdAsync(int messageId)
        {
            return await _messageRepository.GetMessageByIdAsync(messageId);
        }

        /// <summary>
        /// Delete message
        /// </summary>
        public async Task<bool> DeleteMessageAsync(int messageId)
        {
            return await _messageRepository.DeleteMessageAsync(messageId);
        }

        /// <summary>
        /// Get message count for session
        /// </summary>
        public async Task<int> GetSessionMessageCountAsync(string sessionId)
        {
            return await _messageRepository.GetSessionMessageCountBySessionIdStringAsync(sessionId);
        }

        /// <summary>
        /// Get all messages by user
        /// </summary>
        public async Task<List<Models.Message>> GetUserMessagesAsync(string authorMatricNo)
        {
            var author = await _userService.GetUserByMatricNoAsync(authorMatricNo);
            if (author == null)
                return new List<Models.Message>();

            return await _messageRepository.GetUserMessagesAsync(author.Id);
        }
    }
}
