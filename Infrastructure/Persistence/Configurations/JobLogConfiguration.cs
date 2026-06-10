using Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class JobLogConfiguration : IEntityTypeConfiguration<JobLog>
{
    public void Configure(EntityTypeBuilder<JobLog> builder)
    {
        builder.ToTable("job_logs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(l => l.JobId).HasColumnName("job_id");

        builder.Property(l => l.Event)
            .HasColumnName("event")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(l => l.Message).HasColumnName("message");

        builder.Property(l => l.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb")
            .IsRequired(false);

        builder.Property(l => l.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(l => new { l.JobId, l.CreatedAt })
            .HasDatabaseName("idx_job_logs_job_id_created");
    }
}