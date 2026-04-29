using HiveLog.Api.Features.Logs.Models;
using HiveLog.Api.Features.Rules.Models;
using HiveLog.Api.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace HiveLog.Api.Persistence;

public class HiveLogDbContext : DbContext
{
    public HiveLogDbContext(DbContextOptions<HiveLogDbContext> options) : base(options)
    {
    }

    public DbSet<LogEntry> LogEntries { get; set; } = null!;
    public DbSet<WebhookRule> WebhookRules { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new LogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookRuleConfiguration());
    }
}
