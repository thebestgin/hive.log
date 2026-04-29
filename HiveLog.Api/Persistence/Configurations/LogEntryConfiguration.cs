using HiveLog.Api.Features.Logs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiveLog.Api.Persistence.Configurations;

// WARNING: Raw SQL dependency — LogEntryCopyWriter
// LogEntryCopyWriter (Features/Ingest/LogEntryCopyWriter.cs) writes log_entries via
// Npgsql COPY. It reads table/column names from EF metadata, but the Write calls in
// WriteBatchAsync must be kept in the exact same order as the columns listed here.
// Adding a property to LogEntry requires TWO changes in LogEntryCopyWriter:
//   1. Add Col(...) to BuildCopyCommand
//   2. Add WriteAsync/WriteNullableAsync to WriteBatchAsync (same position)
// Missing either causes a column offset — data lands in the wrong columns silently.
public class LogEntryConfiguration : IEntityTypeConfiguration<LogEntry>
{
    public void Configure(EntityTypeBuilder<LogEntry> builder)
    {
        // Composite PK: TimescaleDB requires the hypertable dimension (timestamp) in the PK
        builder.HasKey(e => new { e.Timestamp, e.Id });

        builder.Property(e => e.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.Stream)
            .HasDefaultValue("app");

        builder.Property(e => e.Properties)
            .HasColumnType("jsonb");

        builder.Property(e => e.Exception)
            .HasColumnType("jsonb");

        builder.Property(e => e.Tags)
            .HasColumnType("text[]");

        // W3C Trace Context: supports "all logs in trace X" and "children of span Y"
        builder.HasIndex(e => new { e.TraceId, e.ParentSpanId })
            .HasDatabaseName("ix_log_entries_trace_id_parent_span_id");
    }
}
