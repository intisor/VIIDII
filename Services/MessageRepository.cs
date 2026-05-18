using VIIDII.Data;
using VIIDII.Models;
using Microsoft.EntityFrameworkCore;

namespace VIIDII.Services
{
    /// <summary>
    /// Repository for message persistence operations via DbContext
    /// Works with VIIDII.Models.Message entity for EF Core persistence
    /// </summary>
    public class MessageRepository
    {
        private readonly ViidiiDbContext _context;

        public MessageRepository(ViidiiDbContext context)
        {
            _context = context;
        }

        public async Task<Models.Message> CreateMessageAsync(Models.Message message)
        {
            await _context.Messages.AddAsync(message);
            await _context.SaveChangesAsync();
            return message;
        }

        public async Task<Models.Message?> GetMessageByIdAsync(int messageId)
        {
            return await _context.Messages
                .Include(m => m.Author)
                .Include(m => m.Parent)
                .Include(m => m.Replies)
                .FirstOrDefaultAsync(m => m.Id == messageId);
        }

        public async Task<List<Models.Message>> GetSessionMessagesBySessionIdStringAsync(string sessionId)
        {
            return await _context.Messages
                .Where(m => m.Session.SessionId == sessionId && m.ParentId == null)
                .Include(m => m.Author)
                .Include(m => m.Replies)
                    .ThenInclude(r => r.Author)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Models.Message>> GetSessionMessagesAsync(int sessionId)
        {
            return await _context.Messages
                .Where(m => m.SessionId == sessionId && m.ParentId == null)
                .Include(m => m.Author)
                .Include(m => m.Replies)
                    .ThenInclude(r => r.Author)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Models.Message>> GetMessageRepliesAsync(int parentId)
        {
            return await _context.Messages
                .Where(m => m.ParentId == parentId)
                .Include(m => m.Author)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<Models.Message?> UpdateMessageAsync(Models.Message message)
        {
            _context.Messages.Update(message);
            await _context.SaveChangesAsync();
            return message;
        }

        public async Task<bool> DeleteMessageAsync(int messageId)
        {
            var message = await _context.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
            if (message == null)
                return false;

            _context.Messages.Remove(message);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> GetSessionMessageCountBySessionIdStringAsync(string sessionId)
        {
            return await _context.Messages
                .CountAsync(m => m.Session.SessionId == sessionId);
        }

        public async Task<int> GetSessionMessageCountAsync(int sessionId)
        {
            return await _context.Messages
                .CountAsync(m => m.SessionId == sessionId);
        }

        public async Task<List<Models.Message>> GetUserMessagesAsync(int userId)
        {
            return await _context.Messages
                .Where(m => m.AuthorId == userId)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();
        }
    }
}
