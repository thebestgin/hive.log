using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HiveLog.Api.Persistence;

/// <summary>
/// Design-time factory used by EF Core tools (dotnet ef migrations add).
/// Uses a dummy connection string — migrations do not need a live database.
/// </summary>
public class HiveLogDbContextFactory : IDesignTimeDbContextFactory<HiveLogDbContext>
{
    public HiveLogDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<HiveLogDbContext>();
        // Design-time only — no real DB connection needed for migration generation
        optionsBuilder
            .UseNpgsql("Host=localhost;Database=__design_time_placeholder__")
            .UseSnakeCaseNamingConvention();
        return new HiveLogDbContext(optionsBuilder.Options);
    }
}
