using FutaMeetWeb.Models;
using Microsoft.AspNetCore.Identity;
namespace FutaMeetWeb.Services
{
    public class MockApiService
    {
        private static readonly PasswordHasher<User> _passwordHasher = new();

        private static readonly List<User> _users =
        [
            CreateUser("Intisor", "123456", Role.Student, User.Departments.SoftwareEngineering, User.Levels.Level200, "studpass1"),
                CreateUser("Goodluck", "654321", Role.Student, User.Departments.SoftwareEngineering, User.Levels.Level200, "studpass1"),
                CreateUser("Ade", "789012", Role.Student, User.Departments.SoftwareEngineering, User.Levels.Level200, "studpass1"),
                CreateUser("Umar", "383012", Role.Student, User.Departments.MiningEngineering, User.Levels.Level200, "studpass1"),
                CreateUser("Alice", "100001", Role.Student, User.Departments.ComputerScience, User.Levels.Level100, "studpass1"),
                CreateUser("Brian", "100002", Role.Student, User.Departments.MechanicalEngineering, User.Levels.Level100, "studpass1"),
                CreateUser("Cynthia", "100003", Role.Student, User.Departments.Architecture, User.Levels.Level100, "studpass1"),
                CreateUser("John doe", "Lec001", Role.Lecturer, null, null, "studpass1"),
                CreateUser("Dr. Brown", "Lec002", Role.Lecturer, null, null, "studpass1"),
                CreateUser("Admin", "Admin", Role.Admin, null, null, "studpass1")
        ];

        private static User CreateUser(string name, string matricNo, Role role, User.Departments? department, User.Levels? level, string password)
        {
            var user = new User
            {
                Name = name,
                MatricNo = matricNo,
                Role = role,
                Department = department,
                Level = level
            };
            user.Password = _passwordHasher.HashPassword(user, password);
            return user;
        }

        public static List<User> GetUsers() => _users;
        public static List<User> GetStudents() => [.. _users.Where(user => user.Role == Role.Student)];
        public static List<User> GetLecturers() => _users.Where(user => user.Role == Role.Lecturer).ToList();
        public static Dictionary<TKey, List<User>> GroupUsersBy<TKey>(Func<User, TKey> keySelector)
            => _users
                .GroupBy(keySelector)
                .ToDictionary(group => group.Key, group => group.ToList());
    }
}