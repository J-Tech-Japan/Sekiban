using Microsoft.EntityFrameworkCore;

namespace AspireEventSample.ReadModels;

public class BranchDbContext : DbContext
{
    public BranchDbContext(DbContextOptions<BranchDbContext> options) : base(options)
    {
    }

    public DbSet<BranchDbRecord> Branches { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BranchDbRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.TargetId).IsRequired();
            entity.Property(e => e.RootPartitionKey).IsRequired();
            entity.Property(e => e.AggregateGroup).IsRequired();
            entity.Property(e => e.LastSortableUniqueId).IsRequired();
            entity.Property(e => e.TimeStamp).IsRequired();
            entity.Property(e => e.Name).IsRequired();

            // Create a composite index for faster lookups
            entity.HasIndex(e => new { e.RootPartitionKey, e.AggregateGroup, e.TargetId });
        });
    }
}
