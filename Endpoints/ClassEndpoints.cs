using Microsoft.EntityFrameworkCore;
using VIIDII.Data;
using VIIDII.Models;
using VIIDII.Services;

namespace VIIDII.Endpoints;

/// <summary>
/// Class endpoints. Lecturer-first by design: a lecturer creates a class, uploads
/// the student list once, and runs sessions against it.
/// </summary>
public static class ClassEndpoints
{
    public static void MapClassEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/classes").RequireAuthorization();

        // NOTE: the collection root is mapped on `app`, not on the group.
        // `group.MapGet("")` registers the route as "/api/classes/" with a trailing
        // slash, so "/api/classes" answers with a 302 redirect. The test client
        // follows it and lands on the Blazor shell, which returns 200 HTML — so an
        // unauthenticated caller saw the app document instead of a 401.
        app.MapGet("/api/classes", async (
            ClassService classService,
            CurrentUserAccessor currentUser,
            ViidiiDbContext context) =>
        {
            var user = await ResolveUserAsync(context, currentUser.MatricNo);
            if (user is null) return Results.Unauthorized();

            var owned = await classService.GetForLecturerAsync(user.Id);

            var result = new List<object>();
            foreach (var item in owned)
            {
                var studentCount = await classService.GetStudentCountAsync(item.Id);
                result.Add(new
                {
                    item.Id,
                    item.Code,
                    item.Name,
                    item.PresetType,
                    item.JoinPrompt,
                    item.EnableWatermark,
                    hasList = item.RosterMappingJson is not null || studentCount > 0,
                    studentCount,
                    sessionCount = await classService.GetSessionCountAsync(item.Id)
                });
            }

            return Results.Ok(result);
        }).RequireAuthorization();

        app.MapPost("/api/classes", async (
            CreateClassRequest request,
            ClassService classService,
            CurrentUserAccessor currentUser,
            ViidiiDbContext context) =>
        {
            var user = await ResolveUserAsync(context, currentUser.MatricNo);
            if (user is null) return Results.Unauthorized();

            // Students have no account, so they never reach this. Guard anyway.
            if (user.Role == Role.Student)
            {
                return Results.Forbid();
            }

            var (created, error) = await classService.CreateAsync(
                user.Id,
                request.Code ?? string.Empty,
                request.Name ?? string.Empty,
                request.PresetType,
                request.JoinPrompt,
                request.EnableWatermark ?? true);

            if (created is null)
            {
                return Results.BadRequest(new { error });
            }

            return Results.Ok(new
            {
                created.Id,
                created.Code,
                created.Name,
                created.PresetType,
                created.JoinPrompt,
                created.EnableWatermark
            });
        }).RequireAuthorization();

        group.MapPatch("/{classId:int}", async (
            int classId,
            UpdateClassRequest request,
            ClassService classService,
            CurrentUserAccessor currentUser,
            ViidiiDbContext context) =>
        {
            var user = await ResolveUserAsync(context, currentUser.MatricNo);
            if (user is null) return Results.Unauthorized();

            var item = await classService.GetByIdAsync(classId);
            if (item is null) return Results.NotFound(new { error = "Class not found." });

            if (user.Role != Role.Admin && item.LecturerId != user.Id)
            {
                return Results.Forbid();
            }

            await classService.UpdatePresetAsync(classId, request.PresetType, request.JoinPrompt, request.EnableWatermark);
            return Results.Ok(new { success = true });
        });

        group.MapGet("/suggest-code", async (string? baseCode, ClassService classService) =>
            Results.Ok(new { code = await classService.SuggestCodeAsync(baseCode ?? string.Empty) }));

        group.MapGet("/{classId:int}", async (
            int classId,
            ClassService classService,
            CurrentUserAccessor currentUser,
            ViidiiDbContext context) =>
        {
            var user = await ResolveUserAsync(context, currentUser.MatricNo);
            if (user is null) return Results.Unauthorized();

            var item = await classService.GetByIdAsync(classId);
            if (item is null) return Results.NotFound(new { error = "Class not found." });

            if (user.Role != Role.Admin && item.LecturerId != user.Id)
            {
                return Results.Forbid();
            }

            var studentCount = await classService.GetStudentCountAsync(classId);
            return Results.Ok(new
            {
                item.Id,
                item.Code,
                item.Name,
                item.PresetType,
                item.JoinPrompt,
                item.EnableWatermark,
                hasList = item.RosterMappingJson is not null || studentCount > 0,
                studentCount,
                sessionCount = await classService.GetSessionCountAsync(classId),
                joinUrl = $"/join/{item.Code}"
            });
        });

        group.MapDelete("/{classId:int}", async (
            int classId,
            ClassService classService,
            CurrentUserAccessor currentUser,
            ViidiiDbContext context) =>
        {
            var user = await ResolveUserAsync(context, currentUser.MatricNo);
            if (user is null) return Results.Unauthorized();

            var item = await classService.GetByIdAsync(classId);
            if (item is null) return Results.NotFound(new { error = "Class not found." });

            if (user.Role != Role.Admin && item.LecturerId != user.Id)
            {
                return Results.Forbid();
            }

            context.Classes.Remove(item);
            await context.SaveChangesAsync();

            return Results.NoContent();
        });
    }

    /// <summary>
    /// Public lookup by join code, for the student screen before they type anything.
    /// Returns labels only, never the student list.
    /// </summary>
    public static void MapPublicClassEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/public/class/{code}", async (string code, ClassService classService) =>
        {
            var item = await classService.GetByCodeAsync(code);
            if (item is null)
            {
                return Results.NotFound(new { error = "No class was found with that code." });
            }

            var preset = SpacePreset.GetMeta(item.PresetType);

            return Results.Ok(new
            {
                item.Code,
                item.Name,
                item.PresetType,
                item.EnableWatermark,
                hostLabel = preset.HostLabel,
                attendeeLabel = preset.AttendeeLabel,
                prompt = string.IsNullOrWhiteSpace(item.JoinPrompt)
                    ? preset.DefaultPrompt
                    : item.JoinPrompt,
                hasList = item.RosterMappingJson is not null || (await classService.GetStudentCountAsync(item.Id)) > 0
            });
        });
    }

    private static async Task<User?> ResolveUserAsync(ViidiiDbContext context, string? matricNo)
    {
        if (string.IsNullOrEmpty(matricNo)) return null;
        return await context.Users.FirstOrDefaultAsync(u => u.MatricNo == matricNo);
    }

    private sealed class CreateClassRequest
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? PresetType { get; set; }
        public string? JoinPrompt { get; set; }
        public bool? EnableWatermark { get; set; }
    }

    private sealed class UpdateClassRequest
    {
        public string? PresetType { get; set; }
        public string? JoinPrompt { get; set; }
        public bool? EnableWatermark { get; set; }
    }
}