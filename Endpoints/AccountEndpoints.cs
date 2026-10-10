using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services;

namespace VIIDII.Endpoints;

/// <summary>
/// Lecturer self-service. No institutional approval, no role assignment ceremony.
/// A lecturer signs up and can immediately create a class and run a session.
/// </summary>
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account");

        group.MapPost("/register", async (
            RegisterRequest request,
            HttpContext http,
            ViidiiDbContext context,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("VIIDII.Account");

            var name = request.Name?.Trim() ?? string.Empty;
            var matricNo = request.MatricNo?.Trim() ?? string.Empty;
            var password = request.Password ?? string.Empty;

            if (name.Length is < 2 or > 100)
            {
                return Results.BadRequest(new { error = "Enter your name." });
            }

            if (matricNo.Length is < 3 or > 20)
            {
                return Results.BadRequest(new { error = "Enter a staff or matric number between 3 and 20 characters." });
            }

            if (password.Length < 8)
            {
                return Results.BadRequest(new { error = "Passwords must be at least 8 characters." });
            }

            // Lecturer-first: new accounts are lecturers. Students never register.
            if (await context.Users.AnyAsync(u => u.MatricNo == matricNo))
            {
                return Results.BadRequest(new { error = "That number is already registered." });
            }

            var hasher = http.RequestServices.GetRequiredService<PasswordHasher<User>>();
            var user = new User
            {
                Name = name,
                MatricNo = matricNo,
                PasswordHash = hasher.HashPassword(null!, password),
                Role = Role.Lecturer,
                CreatedAt = DateTime.UtcNow
            };

            context.Users.Add(user);
            await context.SaveChangesAsync();

            var principal = ViidiiPrincipalBuilder.Build(user);
            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true });

            logger.LogInformation("Lecturer registered: {MatricNo}", user.MatricNo);

            return Results.Ok(new { user.Id, user.Name, user.MatricNo, role = user.Role.ToString() });
        });

        group.MapGet("/me", async (CurrentUserAccessor accessor, ViidiiDbContext context) =>
        {
            var user = await accessor.GetUserAsync();
            if (user is null) return Results.Unauthorized();

            return Results.Ok(new
            {
                user.Id,
                user.Name,
                user.MatricNo,
                role = user.Role.ToString()
            });
        });

        // Counts shown on the lecturer home screen.
        group.MapGet("/summary", async (CurrentUserAccessor accessor, ViidiiDbContext context) =>
        {
            var user = await accessor.GetUserAsync();
            if (user is null) return Results.Unauthorized();

            var classCount = await context.Classes.CountAsync(c => c.LecturerId == user.Id);
            var sessionCount = await context.Sessions.CountAsync(s => s.LecturerId == user.Id);

            return Results.Ok(new
            {
                classCount,
                sessionCount,
                hasPassword = !string.IsNullOrEmpty(user.PasswordHash)
            });
        });
    }

    private sealed class RegisterRequest
    {
        public string? Name { get; set; }
        public string? MatricNo { get; set; }
        public string? Password { get; set; }
    }
}