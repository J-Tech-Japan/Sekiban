using Microsoft.EntityFrameworkCore;
using Sekiban.Dcb.Postgres.DbModels;
namespace Sekiban.Dcb.Postgres;

public class SekibanDcbDbContext : DbContext
{
    public DbSet<DbEvent> Events { get; set; } = default!;
    public DbSet<DbTag> Tags { get; set; } = default!;

    public SekibanDcbDbContext(DbContextOptions<SekibanDcbDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Events table
        modelBuilder.Entity<DbEvent>(entity =>
        {
            entity.ToTable("dcb_events");
            entity.HasKey(e => e.Id);

            // SortableUniqueId is the primary ordering column
            entity.HasIndex(e => e.SortableUniqueId).IsUnique().HasDatabaseName("IX_Events_SortableUniqueId");

            entity.HasIndex(e => e.EventType);
            entity.HasIndex(e => e.Timestamp);

            // Configure JSON column for Payload
            entity.Property(e => e.Payload).HasColumnType("json");

            // Configure Tags as JSON array
            entity.Property(e => e.Tags).HasColumnType("jsonb");

            // Ensure proper ordering
            entity.Property(e => e.SortableUniqueId).IsRequired().HasMaxLength(100);
        });

        // Configure Tags table
        modelBuilder.Entity<DbTag>(entity =>
        {
            entity.ToTable("dcb_tags");
            entity.HasKey(t => t.Id);

            // Indexes for efficient querying
            entity.HasIndex(t => t.Tag).HasDatabaseName("IX_Tags_Tag");

            // SortableUniqueId for ordering
            entity.HasIndex(t => t.SortableUniqueId).HasDatabaseName("IX_Tags_SortableUniqueId");

            entity.HasIndex(t => t.EventId).HasDatabaseName("IX_Tags_EventId");

            // Composite index for tag queries ordered by SortableUniqueId
            entity.HasIndex(t => new { t.Tag, t.SortableUniqueId }).HasDatabaseName("IX_Tags_Tag_SortableUniqueId");

            // Ensure proper ordering
            entity.Property(t => t.SortableUniqueId).IsRequired().HasMaxLength(100);
        });
    }
}
