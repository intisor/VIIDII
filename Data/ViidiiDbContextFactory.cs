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
/// </summary>
public class ViidiiDbContextFactory : IDesignTimeDbContextFactory<ViidiiDbContext>
{
    public ViidiiDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ViidiiDbContext>();

        // Design-time only. SQLite requires no server, so `dotnet ef` works on a
        // fresh clone with nothing installed. Migrations generated here are
        // provider-neutral as long as the model avoids provider-specific SQL —
        // which is why the UTC defaults are chosen per provider in OnModelCreating.

        var provider = Environment.GetEnvironmentVariable("VIIDII_DESIGN_PROVIDER")
                       ?? "sqlite";

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