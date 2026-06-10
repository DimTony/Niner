using Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class JobDependencyConfiguration : IEntityTypeConfiguration<JobDependency>
{
    public void Configure(EntityTypeBuilder<JobDependency> builder)
    {
        builder.ToTable("job_dependencies");
        builder.HasKey(d => new { d.JobId, d.DependsOnId });

        builder.Property(d => d.JobId).HasColumnName("job_id");
        builder.Property(d => d.DependsOnId).HasColumnName("depends_on_id");

        builder.HasIndex(d => d.DependsOnId)
            .HasDatabaseName("idx_job_dependencies_depends_on");
    }
}