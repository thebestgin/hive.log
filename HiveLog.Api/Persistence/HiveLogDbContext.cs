using HiveLog.Api.Features.Logs.Models;
using HiveLog.Api.Features.Rules.Models;
using HiveLog.Api.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace HiveLog.Api.Persistence;

// WARNING: Raw SQL dependencies — keep in sync when changing LogEntry schema.
//
// 1. LogEntryCopyWriter (Features/Ingest/LogEntryCopyWriter.cs)
//    Writes log_entries via Npgsql COPY (not EF). Reads table/column names from EF
//    metadata, but Write calls in WriteBatchAsync must be manually kept in sync.
//    → New field on LogEntry: update LogEntryCopyWriter in TWO places.
//
// 2. QueryBuilder (Features/Query/QueryBuilder.cs)
//    Builds SELECT/WHERE via raw SQL. Column list in SELECT is maintained manually.
//    → New queryable field on LogEntry: update QueryBuilder + QueryRequest.
//
// Naming: UseSnakeCaseNamingConvention() is active globally (Program.cs).
// All table and column names are automatically converted to snake_case.
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
