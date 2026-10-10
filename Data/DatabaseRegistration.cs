using Microsoft.EntityFrameworkCore;
using VIIDII.Data;

namespace VIIDII.Data;

/// <summary>
/// Supported database providers, chosen at runtime from configuration.
/// </summary>
public enum DatabaseProvider
{
    /// <summary>SQLite file. Zero setup, ideal for local development and demos.</summary>
    Sqlite,

    /// <summary>PostgreSQL. Recommended for production — see docs/DATABASE_PROVIDERS.md.</summary>
    Postgres,

    /// <summary>SQL Server / Azure SQL. Retained for existing deployments.</summary>
    SqlServer
}

/// <summary>
/// Registers the EF Core provider from configuration.
///
/// The provider is a deployment decision, so it must not be a compile-time one.
/// Requiring every developer to run SQL Server LocalDB was blocking local work and
/// forcing SQL Server-specific SQL into the model (GETUTCDATE, and so on).
/// </summary>
public static class DatabaseRegistration
{
    public static IServiceCollection AddViidiiDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var provider = ResolveProvider(configuration, environment);
        var connectionString = ResolveConnectionString(configuration, provider, environment);

        services.AddDbContext<ViidiiDbContext>(options =>
        {
            switch (provider)
            {
                case DatabaseProvider.Sqlite:
                    options.UseSqlite(connectionString);
                    break;

                case DatabaseProvider.Postgres:
                    options.UseNpgsql(connectionString);
                    break;

                case DatabaseProvider.SqlServer:
                    options.UseSqlServer(connectionString);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported database provider: {provider}.");
            }
        });

        return services;
    }

    /// <summary>
    /// Reads the provider from configuration, defaulting to SQLite outside production
    /// so a fresh clone runs with no database server at all.
    /// </summary>
    public static DatabaseProvider ResolveProvider(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Database:Provider"];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return environment.IsProduction()
                ? throw new InvalidOperationException(
                    "Database:Provider must be set in production. Use 'Postgres', 'SqlServer' or 'Sqlite'.")
                : DatabaseProvider.Sqlite;
        }

        return configured.Trim().ToLowerInvariant() switch
        {
            "sqlite" => DatabaseProvider.Sqlite,
            "postgres" or "postgresql" or "npgsql" => DatabaseProvider.Postgres,
            "sqlserver" or "mssql" => DatabaseProvider.SqlServer,
            _ => throw new InvalidOperationException(
                $"Unknown Database:Provider '{configured}'. Use 'Postgres', 'SqlServer' or 'Sqlite'.")
        };
    }

    private static string ResolveConnectionString(
        IConfiguration configuration,
        DatabaseProvider provider,
        IHostEnvironment environment)
    {
        var configured = configuration.GetConnectionString("DefaultConnection");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        // A deliberate fallback rather than a throw: `dotnet run` should work on a
        // fresh clone without any database server installed.
        if (provider == DatabaseProvider.Sqlite && !environment.IsProduction())
        {
            return "Data Source=viidii.db";
        }

        throw new InvalidOperationException(
            "ConnectionStrings:DefaultConnection is not configured.");
    }

    /// <summary>
    /// SQL Server's GETUTCDATE() is not portable. Every provider has its own
    /// equivalent, and SQLite (used for tests) does not support any of the others.
    ///
    /// These defaults exist only as a safety net for rows inserted outside the
    /// application; every write path sets the value explicitly.
    /// </summary>
    public static string UtcNowDefaultSql(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer => "GETUTCDATE()",
        DatabaseProvider.Postgres => "CURRENT_TIMESTAMP",
        DatabaseProvider.Sqlite => "CURRENT_TIMESTAMP",
        _ => "CURRENT_TIMESTAMP"
    };
}