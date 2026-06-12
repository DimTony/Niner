using Core.Entities;
using Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(j => j.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(j => j.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(j => j.Priority)
            .HasColumnName("priority")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(j => j.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(j => j.RetryCount)
            .HasColumnName("retry_count")
            .HasDefaultValue(0);

        builder.Property(j => j.MaxRetries)
            .HasColumnName("max_retries")
            .HasDefaultValue(3);

        builder.Property(j => j.ScheduledAt)
            .HasColumnName("scheduled_at");

        builder.Property(j => j.Recurrence)
            .HasColumnName("recurrence")
            .HasConversion<string>()
            .IsRequired(false);

        builder.Property(j => j.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(j => j.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(j => j.DeletedAt)
            .HasColumnName("deleted_at")
            .IsRequired(false);

        builder.Property(j => j.LockedBy)
            .HasColumnName("locked_by")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(j => j.LockedAt)
            .HasColumnName("locked_at")
            .IsRequired(false);

        builder.Property(j => j.LastError)
            .HasColumnName("last_error")
            .IsRequired(false);

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Relationships
        builder.HasMany(j => j.Dependencies)
            .WithOne(d => d.Job)
            .HasForeignKey(d => d.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(j => j.Dependents)
            .WithOne(d => d.DependsOn)
            .HasForeignKey(d => d.DependsOnId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(j => j.Logs)
            .WithOne(l => l.Job)
            .HasForeignKey(l => l.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(j => j.DeadLetterEntry)
            .WithOne(d => d.Job)
            .HasForeignKey<DeadLetterEntry>(d => d.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(j => new { j.Status, j.ScheduledAt })
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("idx_jobs_status_scheduled_at");

        builder.HasIndex(j => j.ScheduledAt)
            .HasFilter("status = 'Pending' AND deleted_at IS NULL")
            .HasDatabaseName("idx_jobs_scheduled_pending");

        builder.HasIndex(j => new { j.Priority, j.ScheduledAt, j.CreatedAt })
            .HasFilter("status = 'Pending' AND deleted_at IS NULL")
            .HasDatabaseName("idx_jobs_priority_scheduled_created");

        builder.HasIndex(j => j.LockedAt)
            .HasFilter("locked_by IS NOT NULL AND deleted_at IS NULL")
            .HasDatabaseName("idx_jobs_locked_at");
    }
}