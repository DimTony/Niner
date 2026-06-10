using Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class DeadLetterEntryConfiguration : IEntityTypeConfiguration<DeadLetterEntry>
{
    public void Configure(EntityTypeBuilder<DeadLetterEntry> builder)
    {
        builder.ToTable("dead_letter_queue");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(d => d.JobId).HasColumnName("job_id");
        builder.Property(d => d.ErrorDetails).HasColumnName("error_details");
        builder.Property(d => d.FailureCount).HasColumnName("failure_count");
        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(d => d.Resolved).HasColumnName("resolved").HasDefaultValue(false);

        builder.HasIndex(d => new { d.Resolved, d.CreatedAt })
            .HasDatabaseName("idx_dlq_resolved");
    }
}