using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using VIIDII.Models;
using VIIDII.Services;
using VIIDII.Tests.TestInfrastructure;
using Xunit;

namespace VIIDII.Tests.Services;

/// <summary>
/// Tests for the authentication and authorization hardening work.
///
/// These cover the specific defects found in review:
///  - no authentication pipeline existed at all
///  - identity was read from a mutable session string rather than signed claims
///  - login errors would reveal whether an account existed
/// </summary>
public class AuthServiceTests
{
    private static (AuthService Auth, CurrentUserAccessor Accessor, TestDbContextFactory Factory, string Password)
        BuildSut(Role role)
    {
        var factory = new TestDbContextFactory();
        var services = new ServiceCollection();

        // One scoped context per "request" — mirrors real DI behaviour.
        services.AddScoped(_ => factory.CreateDbContext());
        services.AddScoped<UserService>();
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<IHttpContextAccessor, HttpContextAccessor>();
        services.AddSingleton<PasswordHasher<User>>();

        var provider = services.BuildServiceProvider();

        const string password = "correct-horse-battery-staple";
        var hasher = provider.GetRequiredService<PasswordHasher<User>>();

        using (var seedContext = factory.CreateDbContext())
        {
            var user = new User
            {
                Name = "Auth Test User",
                MatricNo = $"TEST-{role}",
                PasswordHash = hasher.HashPassword(null!, password),
                Role = role,
                Department = User.Departments.SoftwareEngineering,
                Level = User.Levels.Level300
            };

            seedContext.Users.Add(user);
            seedContext.SaveChanges();
        }

        var accessor = ActivatorUtilities.CreateInstance<CurrentUserAccessor>(provider);
        var auth = new AuthService(
            provider.GetRequiredService<UserService>(),
            hasher,
            accessor);

        return (auth, accessor, factory, password);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ReturnsSuccess_ForCorrectPassword()
    {
        var (auth, _, factory, password) = BuildSut(Role.Student);
        using var _ = factory;

        var (success, message, user) = await auth.ValidateCredentialsAsync("TEST-Student", password);

        Assert.True(success);
        Assert.Null(message);
        Assert.NotNull(user);
        Assert.Equal(Role.Student, user!.Role);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ReturnsFailure_ForWrongPassword()
    {
        var (auth, _, factory, _) = BuildSut(Role.Student);
        using var _ = factory;

        var (success, message, user) = await auth.ValidateCredentialsAsync("TEST-Student", "wrong-password");

        Assert.False(success);
        Assert.NotNull(message);
        Assert.Null(user);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_DoesNotRevealWhetherAccountExists()
    {
        var (auth, _, factory, _) = BuildSut(Role.Student);
        using var _ = factory;

        var (_, unknownUserMessage, _) = await auth.ValidateCredentialsAsync("NO-SUCH-USER", "any-password");
        var (_, wrongPasswordMessage, _) = await auth.ValidateCredentialsAsync("TEST-Student", "wrong-password");

        // Identical messages: an attacker must not be able to enumerate valid matric numbers.
        Assert.Equal(unknownUserMessage, wrongPasswordMessage);
        Assert.Equal("Invalid credentials.", unknownUserMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateCredentialsAsync_RejectsBlankCredentials(string matricNo)
    {
        var (auth, _, factory, _) = BuildSut(Role.Student);
        using var _ = factory;

        var (success, message, user) = await auth.ValidateCredentialsAsync(matricNo, "some-password");

        Assert.False(success);
        Assert.NotNull(message);
        Assert.Null(user);
    }

    [Fact]
    public async Task GetCurrentUserAsync_ReturnsNull_WhenRequestIsAnonymous()
    {
        var (auth, _, factory, _) = BuildSut(Role.Student);
        using var _ = factory;

        var current = await auth.GetCurrentUserAsync();

        Assert.Null(current);
        Assert.False(auth.IsAuthenticated());
    }
}

/// <summary>
/// Tests for claim issuance and policy definitions.
/// </summary>
public class IdentityTests
{
    [Fact]
    public void PrincipalBuilder_IssuesMatricNoNameAndRole()
    {
        var user = new User
        {
            Id = 42,
            Name = "Dr. Lecturer",
            MatricNo = "LEC999",
            PasswordHash = "hash",
            Role = Role.Lecturer,
            Department = User.Departments.ComputerScience,
            Level = User.Levels.Level400
        };

        var principal = ViidiiPrincipalBuilder.Build(user);

        Assert.True(principal.Identity?.IsAuthenticated);
        Assert.Equal("LEC999", principal.FindFirst(ViidiiClaims.MatricNo)?.Value);
        Assert.Equal("Dr. Lecturer", principal.FindFirst(ViidiiClaims.DisplayName)?.Value);
        Assert.Equal(Role.Lecturer.ToString(), principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value);
        Assert.Equal("42", principal.FindFirst(ViidiiClaims.UserId)?.Value);
    }

    [Fact]
    public void PrincipalBuilder_OmitsDepartmentAndLevel_WhenSetToAny()
    {
        var user = new User
        {
            Id = 1,
            Name = "Anyone",
            MatricNo = "ANY001",
            PasswordHash = "hash",
            Role = Role.Student,
            Department = User.Departments.Any,
            Level = User.Levels.Any
        };

        var principal = ViidiiPrincipalBuilder.Build(user);

        // "Any" is a wildcard filter, not a real attribute — it must not be claimed.
        Assert.Null(principal.FindFirst(ViidiiClaims.Department));
        Assert.Null(principal.FindFirst(ViidiiClaims.Level));
    }

    [Theory]
    //                      role,        lecturer, staffOrAdmin, admin
    [InlineData(Role.Lecturer, true, true, false)]
    [InlineData(Role.Student, false, false, false)]
    [InlineData(Role.Admin, false, true, true)]
    public async Task RolePolicies_AllowOnlyTheIntendedRoles(
        Role role, bool lecturerAllowed, bool staffOrAdminAllowed, bool adminAllowed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ViidiiPolicies.LecturerOnly,
                p => p.RequireRole(Role.Lecturer.ToString()));
            options.AddPolicy(ViidiiPolicies.StaffOrAdmin,
                p => p.RequireRole(Role.Lecturer.ToString(), Role.Admin.ToString()));
            options.AddPolicy(ViidiiPolicies.AdminOnly,
                p => p.RequireRole(Role.Admin.ToString()));
        });

        var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        var user = new ClaimsPrincipal(ViidiiPrincipalBuilder.Build(new User
        {
            Id = 1,
            Name = "Policy Test",
            MatricNo = "POL001",
            PasswordHash = "hash",
            Role = role
        }));

        Assert.Equal(lecturerAllowed, (await authorization.AuthorizeAsync(user, null, ViidiiPolicies.LecturerOnly)).Succeeded);
        Assert.Equal(staffOrAdminAllowed, (await authorization.AuthorizeAsync(user, null, ViidiiPolicies.StaffOrAdmin)).Succeeded);
        Assert.Equal(adminAllowed, (await authorization.AuthorizeAsync(user, null, ViidiiPolicies.AdminOnly)).Succeeded);
    }

    [Fact]
    public async Task LecturerOnlyPolicy_RejectsAnonymousUser()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
            options.AddPolicy(ViidiiPolicies.LecturerOnly, p => p.RequireRole(Role.Lecturer.ToString())));

        var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();

        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await authorization.AuthorizeAsync(anonymous, null, ViidiiPolicies.LecturerOnly);

        Assert.False(result.Succeeded);
    }
}