using FutaMeetWeb.Models;
namespace FutaMeetWeb.Services
{
    public class MockApiService
    {
        private static readonly List<User> _users =
            [
                new() { Name = "Intisor", MatricNo = "123456", Role = Role.Student, Department = "Software Engineering" },
                new() { Name = "Goodluck", MatricNo = "654321", Role = Role.Student, Department = "Software Engineering" },
                new() { Name = "Victor", MatricNo = "789012", Role = Role.Student, Department = "Software Engineering" },
                new() { Name = "Umar", MatricNo = "383012", Role = Role.Student, Department = "Mining Engineering" },
                new() { Name = "Festus", MatricNo = "Lec001", Role = Role.Lecturer, Department = null },
                new() { Name = "Dr. Brown", MatricNo = "Lec002", Role = Role.Lecturer, Department = null }
            ];
        public static List<User> GetUsers() => _users;
        public static List<User> GetStudents() => [.. _users.Where(user => user.Role == Role.Student)];
        public static List<User> GetLecturers() => _users.Where(user => user.Role == Role.Lecturer).ToList();
        public static Dictionary<TKey, List<User>> GroupUsersBy<TKey>(Func<User, TKey> keySelector)
            => _users
                .GroupBy(keySelector)
                .ToDictionary(group => group.Key, group => group.ToList());
    }
}