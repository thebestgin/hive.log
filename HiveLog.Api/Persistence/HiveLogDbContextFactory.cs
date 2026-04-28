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
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=hivelog_design;Username=jobdate;Password=jobdate123");
        return new HiveLogDbContext(optionsBuilder.Options);
    }
}
