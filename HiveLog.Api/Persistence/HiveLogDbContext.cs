using HiveLog.Api.Features.Logs.Models;
using HiveLog.Api.Features.Rules.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

        modelBuilder.Entity<LogEntry>(entity =>
        {
            // Composite PK: TimescaleDB requires the hypertable dimension in the PK
            entity.HasKey(e => new { e.Timestamp, e.Id });

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.Stream)
                .HasDefaultValue("app");

            // TEXT[] column type for tags
            entity.Property(e => e.Tags)
                .HasColumnType("text[]");

            // Indexes are created via Raw SQL in migration (IF NOT EXISTS for idempotency)
        });

        modelBuilder.Entity<WebhookRule>(entity =>
        {
            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.TriggerTags)
                .HasColumnType("text[]");
        });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
            return;

        // GssEncryptionMode.Disable is configured via the data source in Program.cs
    }
}
