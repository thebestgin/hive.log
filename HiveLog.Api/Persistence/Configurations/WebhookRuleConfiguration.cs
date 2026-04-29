using HiveLog.Api.Features.Rules.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiveLog.Api.Persistence.Configurations;

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
