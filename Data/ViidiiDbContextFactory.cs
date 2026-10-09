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

        // Design-time only. Never used at runtime — migrations generated against
        // this provider are provider-neutral because the model uses Fluent APIs.
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=viidii_design;Trusted_Connection=true;");

        return new ViidiiDbContext(optionsBuilder.Options);
    }
}