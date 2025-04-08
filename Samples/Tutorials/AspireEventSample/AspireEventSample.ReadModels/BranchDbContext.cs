using Microsoft.EntityFrameworkCore;

namespace AspireEventSample.ReadModels;

public class BranchDbContext : DbContext
{
    public BranchDbContext(DbContextOptions<BranchDbContext> options) : base(options)
    {
    }

    public DbSet<BranchDbRecord> Branches { get; set; } = null!;
    public DbSet<CartDbRecord> Carts { get; set; } = null!;
    public DbSet<CartItemDbRecord> CartItems { get; set; } = null!;

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

        modelBuilder.Entity<CartDbRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.TargetId).IsRequired();
            entity.Property(e => e.RootPartitionKey).IsRequired();
            entity.Property(e => e.AggregateGroup).IsRequired();
            entity.Property(e => e.LastSortableUniqueId).IsRequired();
            entity.Property(e => e.TimeStamp).IsRequired();
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.TotalAmount).IsRequired();

            // Create a composite index for faster lookups
            entity.HasIndex(e => new { e.RootPartitionKey, e.AggregateGroup, e.TargetId });
        });

        modelBuilder.Entity<CartItemDbRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.TargetId).IsRequired();
            entity.Property(e => e.RootPartitionKey).IsRequired();
            entity.Property(e => e.AggregateGroup).IsRequired();
            entity.Property(e => e.LastSortableUniqueId).IsRequired();
            entity.Property(e => e.TimeStamp).IsRequired();
            entity.Property(e => e.CartId).IsRequired();
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Quantity).IsRequired();
            entity.Property(e => e.ItemId).IsRequired();
            entity.Property(e => e.Price).IsRequired();

            // Create indexes for faster lookups
            entity.HasIndex(e => new { e.RootPartitionKey, e.AggregateGroup, e.TargetId });
            entity.HasIndex(e => e.CartId); // Index for cart ID lookups
        });
    }
}
