using VIIDII.Data;
using VIIDII.Models;
using Microsoft.EntityFrameworkCore;

namespace VIIDII.Services
{
    public class UserService
    {
        private readonly ViidiiDbContext _context;

        public UserService(ViidiiDbContext context)
        {
            _context = context;
        }

        public async Task<List<User>> GetUsersAsync()
        {
            return await _context.Users.ToListAsync();
        }

        public async Task<List<User>> GetStudentsAsync()
        {
            return await _context.Users
                .Where(u => u.Role == Role.Student)
                .ToListAsync();
        }

        public async Task<List<User>> GetLecturersAsync()
        {
            return await _context.Users
                .Where(u => u.Role == Role.Lecturer)
                .ToListAsync();
        }

        public async Task<User?> GetUserByMatricNoAsync(string matricNo)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.MatricNo == matricNo);
        }

        public async Task<User?> GetUserByIdAsync(int id)
        {
            return await _context.Users.FindAsync(id);
        }

        /// <summary>
        /// Persists an updated password hash. Used by the rehash path during login.
        /// </summary>
        public async Task UpdatePasswordHashAsync(User user)
        {
            _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }

        public async Task<Dictionary<TKey, List<User>>> GroupUsersByAsync<TKey>(Func<User, TKey> keySelector)
            where TKey : notnull
        {
            var users = await _context.Users.ToListAsync();
            return users
                .GroupBy(keySelector)
                .ToDictionary(group => group.Key, group => group.ToList());
        }
    }
}
