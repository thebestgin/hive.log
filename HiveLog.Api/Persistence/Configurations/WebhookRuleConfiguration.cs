using HiveLog.Api.Features.Rules.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiveLog.Api.Persistence.Configurations;

// WebhookRule is written exclusively via EF Core — no Raw SQL, no COPY.
// Changes here do not require manual synchronization with other files.
public class WebhookRuleConfiguration : IEntityTypeConfiguration<WebhookRule>
{
    public void Configure(EntityTypeBuilder<WebhookRule> builder)
    {
        builder.Property(e => e.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.TriggerTags)
            .HasColumnType("text[]");
    }
}
