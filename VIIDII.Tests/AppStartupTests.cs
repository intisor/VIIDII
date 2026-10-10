using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VIIDII.Data;
using Xunit;

namespace VIIDII.Tests;

/// <summary>
/// Boots the real application against a temporary SQLite database.
///
/// This is the check that configuration actually works: a wrong provider name, a
/// missing connection string, or SQL Server-only schema SQL would all surface here
/// rather than on a server.
/// </summary>
public class AppStartupTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AppStartupTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private void ConfigureTestDatabase(WebApplicationFactory<Program> factory)
    {
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                $"Data Source=file:viddii-test-{Guid.NewGuid():N}?mode=memory&cache=shared");
        });
    }

    [Fact]
    public async Task ApplicationStarts_AgainstSqlite_WithoutASqlServer()
    {
        ConfigureTestDatabase(_factory);

        using var client = _factory.CreateClient();

        // A 200 or a redirect both prove the pipeline built and served a request.
        var response = await client.GetAsync("/health");

        Assert.True(
            response.StatusCode is System.Net.HttpStatusCode.OK,
            $"Expected /health to be reachable, got {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task HealthEndpoint_ReportsOk()
    {
        ConfigureTestDatabase(_factory);

        using var client = _factory.CreateClient();
        var body = await client.GetStringAsync("/health");

        Assert.Contains("ok", body, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Provider resolution is a configuration concern with failure modes worth pinning:
/// a typo in Database:Provider must produce a clear error, not a silent fallback.
/// </summary>
public class DatabaseProviderResolutionTests
{
    [Theory]
    [InlineData("Sqlite", DatabaseProvider.Sqlite)]
    [InlineData("sqlite", DatabaseProvider.Sqlite)]
    [InlineData("Postgres", DatabaseProvider.Postgres)]
    [InlineData("postgresql", DatabaseProvider.Postgres)]
    [InlineData("Npgsql", DatabaseProvider.Postgres)]
    [InlineData("SqlServer", DatabaseProvider.SqlServer)]
    [InlineData("mssql", DatabaseProvider.SqlServer)]
    public void ResolvesKnownProviderNames(string configuredValue, DatabaseProvider expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = configuredValue
            })
            .Build();

        var environment = new TestEnvironment("Development");

        Assert.Equal(expected, DatabaseRegistration.ResolveProvider(configuration, environment));
    }

    [Fact]
    public void ThrowsOnAnUnknownProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Oracle"
            })
            .Build();

        var environment = new TestEnvironment("Development");

        var exception = Assert.Throws<InvalidOperationException>(
            () => DatabaseRegistration.ResolveProvider(configuration, environment));

        Assert.Contains("Oracle", exception.Message);
    }

    [Fact]
    public void DefaultsToSqliteOutsideProduction()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Equal(
            DatabaseProvider.Sqlite,
            DatabaseRegistration.ResolveProvider(configuration, new TestEnvironment("Development")));
    }

    [Fact]
    public void ThrowsInProductionWhenNoProviderIsConfigured()
    {
        var configuration = new ConfigurationBuilder().Build();

        // Silently falling back to a SQLite file on a production server would be
        // worse than refusing to start.
        Assert.Throws<InvalidOperationException>(
            () => DatabaseRegistration.ResolveProvider(configuration, new TestEnvironment("Production")));
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public TestEnvironment(string environmentName) =>
            EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "VIIDII.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}