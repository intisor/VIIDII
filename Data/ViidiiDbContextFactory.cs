using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VIIDII.Data;

/// <summary>
/// Design-time factory used only by the EF Core tooling (migrations, scaffolding).
///
/// Without this, `dotnet ef` has to boot the whole application — which now
/// requires a reachable database and a resolved hosted-service graph — just to
/// read the model. That made migration generation fail for anyone without a
/// local SQL Server instance.
///
/// Note what this does and does not solve: it decides which provider the
/// *scaffolded DDL* targets. It does not make migrations portable. A migration
/// file is written in one provider's type names and cannot run on another.
/// </summary>
public class ViidiiDbContextFactory : IDesignTimeDbContextFactory<ViidiiDbContext>
{
    public ViidiiDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ViidiiDbContext>();

        // Which provider the scaffolded DDL is written for. This defaults to
        // PostgreSQL, the production provider, and that is deliberate.
        //
        // Scaffolding never connects to a database — it only reads the model — so
        // targeting PostgreSQL costs nothing on a fresh clone with no server running.
        // Defaulting to SQLite instead costs a great deal: the generated migration
        // then contains SQLite column types, and applying it to the production
        // PostgreSQL database fails. That has already happened once. The provider can
        // be overridden for anyone who genuinely needs different DDL:
        //
        //     $env:VIIDII_DESIGN_PROVIDER = "postgres"
        //     dotnet ef migrations add <Name>

        var provider = Environment.GetEnvironmentVariable("VIIDII_DESIGN_PROVIDER")
                       ?? "postgres";

        switch (provider.Trim().ToLowerInvariant())
        {
            case "postgres":
            case "postgresql":
                optionsBuilder.UseNpgsql(
                    Environment.GetEnvironmentVariable("VIIDII_DESIGN_POSTGRES")
                    ?? "Host=localhost;Database=viidii_design;Username=postgres;Password=postgres");
                break;

            case "sqlserver":
                optionsBuilder.UseSqlServer(
                    Environment.GetEnvironmentVariable("VIIDII_DESIGN_SQLSERVER")
                    ?? "Server=(localdb)\\mssqllocaldb;Database=viidii_design;Trusted_Connection=true;");
                break;

            default:
                optionsBuilder.UseSqlite(
                    Environment.GetEnvironmentVariable("VIIDII_DESIGN_SQLITE")
                    ?? "Data Source=viidii-design.db");
                break;
        }

        return new ViidiiDbContext(optionsBuilder.Options);
    }
}