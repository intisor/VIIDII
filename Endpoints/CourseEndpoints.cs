using Microsoft.EntityFrameworkCore;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services;

namespace VIIDII.Endpoints;

/// <summary>
/// Course endpoints. Lecturer-first by design: a lecturer creates a course, owns
/// its class list, and runs sessions inside it. Institution and department are
/// deliberately absent and can be attached later without moving data.
/// </summary>
public static class CourseEndpoints
{
    public static void MapCourseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/courses").RequireAuthorization();

        group.MapGet("/", async (CourseService courseService, CurrentUserAccessor currentUser, ViidiiDbContext context) =>
        {
            var user = await ResolveUserAsync(context, currentUser.MatricNo);
            if (user is null) return Results.Unauthorized();

            var courses = await courseService.GetForLecturerAsync(user.Id);

            var results = new List<object>();
            foreach (var course in courses)
            {
                results.Add(new
                {
                    course.Id,
                    course.Code,
                    course.Title,
                    course.JoinPrompt,
                    hasRoster = course.RosterMappingJson is not null,
                    rosterCount = await courseService.GetRosterCountAsync(course.Id),
                    sessionCount = await courseService.GetSessionCountAsync(course.Id),
                    createdAt = course.CreatedAt
                });
            }

            return Results.Ok(results);
        });

        group.MapPost("/", async (
            CreateCourseRequest request,
            CourseService courseService,
            CurrentUserAccessor currentUser,
            ViidiiDbContext context) =>
        {
            var user = await ResolveUserAsync(context, currentUser.MatricNo);
            if (user is null) return Results.Unauthorized();

            // Only lecturers and admins create courses. Students have no account
            // and therefore never reach this endpoint.
            if (user.Role == Role.Student)
            {
                return Results.Forbid();
            }

            var (course, error) = await courseService.CreateAsync(
                user.Id,
                request.Code ?? string.Empty,
                request.Title ?? string.Empty);

            if (course is null)
            {
                return Results.BadRequest(new { error });
            }

            return Results.Ok(new { course.Id, course.Code, course.Title });
        });

        // Suggests an unused code. Offered to the lecturer, never applied silently.
        group.MapGet("/suggest-code", async (string? baseCode, CourseService courseService) =>
            Results.Ok(new { code = await courseService.SuggestCodeAsync(baseCode ?? string.Empty) }));

        group.MapGet("/{courseId:int}", async (
            int courseId,
            CourseService courseService,
            CurrentUserAccessor currentUser,
            ViidiiDbContext context) =>
        {
            var user = await ResolveUserAsync(context, currentUser.MatricNo);
            if (user is null) return Results.Unauthorized();

            var course = await courseService.GetByIdAsync(courseId);
            if (course is null) return Results.NotFound(new { error = "Course not found." });

            // Admins can inspect any course; lecturers only their own.
            if (user.Role != Role.Admin && course.LecturerId != user.Id)
            {
                return Results.Forbid();
            }

            return Results.Ok(new
            {
                course.Id,
                course.Code,
                course.Title,
                course.JoinPrompt,
                hasRoster = course.RosterMappingJson is not null,
                rosterCount = await courseService.GetRosterCountAsync(courseId),
                sessionCount = await courseService.GetSessionCountAsync(courseId),
                joinUrl = $"/join/{course.Code}"
            });
        });

        group.MapDelete("/{courseId:int}", async (
            int courseId,
            CourseService courseService,
            CurrentUserAccessor currentUser,
            ViidiiDbContext context) =>
        {
            var user = await ResolveUserAsync(context, currentUser.MatricNo);
            if (user is null) return Results.Unauthorized();

            var course = await courseService.GetByIdAsync(courseId);
            if (course is null) return Results.NotFound(new { error = "Course not found." });

            if (user.Role != Role.Admin && course.LecturerId != user.Id)
            {
                return Results.Forbid();
            }

            context.Courses.Remove(course);
            await context.SaveChangesAsync();

            return Results.NoContent();
        });
    }

    /// <summary>
    /// Public lookup by join code, for the student join screen before sign-in.
    /// Returns labels only, never the class list.
    /// </summary>
    public static void MapPublicCourseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/public/course/{code}", async (string code, CourseService courseService) =>
        {
            var course = await courseService.GetByCodeAsync(code);
            if (course is null)
            {
                return Results.NotFound(new { error = "No class was found with that code." });
            }

            return Results.Ok(new
            {
                course.Code,
                course.Title,
                prompt = string.IsNullOrWhiteSpace(course.JoinPrompt)
                    ? "Enter your details to join"
                    : course.JoinPrompt,
                hasRoster = course.RosterMappingJson is not null
            });
        });
    }

    private static async Task<User?> ResolveUserAsync(ViidiiDbContext context, string? matricNo)
    {
        if (string.IsNullOrEmpty(matricNo)) return null;
        return await context.Users.FirstOrDefaultAsync(u => u.MatricNo == matricNo);
    }

    private sealed class CreateCourseRequest
    {
        public string? Code { get; set; }
        public string? Title { get; set; }
    }
}