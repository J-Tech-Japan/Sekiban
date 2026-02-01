using Microsoft.EntityFrameworkCore;
using Sekiban.Dcb.Postgres.DbModels;
namespace Sekiban.Dcb.Postgres;

public class SekibanDcbDbContext : DbContext
{
    public DbSet<DbEvent> Events { get; set; } = default!;
    public DbSet<DbTag> Tags { get; set; } = default!;
    public DbSet<DbMultiProjectionState> MultiProjectionStates { get; set; } = default!;

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
            entity.HasKey(e => new { e.ServiceId, e.Id });

            entity.HasIndex(e => e.ServiceId).HasDatabaseName("IX_Events_ServiceId");
            entity.HasIndex(e => new { e.ServiceId, e.SortableUniqueId })
                .HasDatabaseName("IX_Events_Service_SortableUniqueId");

            entity.HasIndex(e => e.EventType);
            entity.HasIndex(e => e.Timestamp);

            // Configure JSON column for Payload
            entity.Property(e => e.Payload).HasColumnType("json");

            // Configure Tags as JSON array
            entity.Property(e => e.Tags).HasColumnType("jsonb");

            // Ensure proper ordering
            entity.Property(e => e.SortableUniqueId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ServiceId).IsRequired().HasMaxLength(64);
        });

        // Configure Tags table
        modelBuilder.Entity<DbTag>(entity =>
        {
            entity.ToTable("dcb_tags");
            entity.HasKey(t => t.Id);

            // Indexes for efficient querying
            entity.HasIndex(t => t.ServiceId).HasDatabaseName("IX_Tags_ServiceId");
            entity.HasIndex(t => new { t.ServiceId, t.Tag }).HasDatabaseName("IX_Tags_Service_Tag");

            // SortableUniqueId for ordering
            entity.HasIndex(t => t.SortableUniqueId).HasDatabaseName("IX_Tags_SortableUniqueId");

            entity.HasIndex(t => t.EventId).HasDatabaseName("IX_Tags_EventId");

            // Composite index for tag queries ordered by SortableUniqueId
            entity.HasIndex(t => new { t.ServiceId, t.Tag, t.SortableUniqueId }).HasDatabaseName("IX_Tags_Service_Tag_SortableUniqueId");

            // Ensure proper ordering
            entity.Property(t => t.SortableUniqueId).IsRequired().HasMaxLength(100);
            entity.Property(t => t.ServiceId).IsRequired().HasMaxLength(64);
        });

        // Configure MultiProjectionStates table
        modelBuilder.Entity<DbMultiProjectionState>(entity =>
        {
            entity.ToTable("dcb_multi_projection_states");

            // Composite primary key
            entity.HasKey(s => new { s.ServiceId, s.ProjectorName, s.ProjectorVersion });

            // Index for projector name queries
            entity.HasIndex(s => new { s.ServiceId, s.ProjectorName })
                .HasDatabaseName("IX_MultiProjectionStates_Service_ProjectorName");

            // Index for updated timestamp
            entity.HasIndex(s => s.UpdatedAt).HasDatabaseName("IX_MultiProjectionStates_UpdatedAt");

            // StateData is stored as bytea
            entity.Property(s => s.StateData).HasColumnType("bytea");

            // Check constraint for offload consistency
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_MultiProjectionStates_OffloadConsistency",
                "(\"IsOffloaded\" = false AND \"StateData\" IS NOT NULL) OR (\"IsOffloaded\" = true AND \"OffloadKey\" IS NOT NULL)"));

            entity.Property(s => s.ServiceId).IsRequired().HasMaxLength(64);
        });
    }
}
