using Core.Entities;
using Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobDependency> JobDependencies => Set<JobDependency>();
    public DbSet<DeadLetterEntry> DeadLetterEntries => Set<DeadLetterEntry>();
    public DbSet<JobLog> JobLogs => Set<JobLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new JobConfiguration());
        modelBuilder.ApplyConfiguration(new JobDependencyConfiguration());
        modelBuilder.ApplyConfiguration(new DeadLetterEntryConfiguration());
        modelBuilder.ApplyConfiguration(new JobLogConfiguration());
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<Job>())
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;

        return base.SaveChangesAsync(ct);
    }
}