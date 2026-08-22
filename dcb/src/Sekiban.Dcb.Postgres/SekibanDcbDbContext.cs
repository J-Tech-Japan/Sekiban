using Microsoft.EntityFrameworkCore;
using Sekiban.Dcb.Postgres.DbModels;
namespace Sekiban.Dcb.Postgres;

public class SekibanDcbDbContext : DbContext
{
    public DbSet<DbEvent> Events { get; set; } = default!;
    public DbSet<DbTag> Tags { get; set; } = default!;
    public DbSet<DbMultiProjectionState> MultiProjectionStates { get; set; } = default!;
    public DbSet<DbTagHead> TagHeads { get; set; } = default!;
    public DbSet<DbTagHeadViolation> TagHeadViolations { get; set; } = default!;
    public DbSet<DbTagHeadEnablementEpoch> TagHeadEnablementEpochs { get; set; } = default!;

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

        // SEK-G40 durable tag-head fence tables. Runtime paths only issue DML against these provisioned tables; migration
        // is the sole DDL owner.
        modelBuilder.Entity<DbTagHead>(entity =>
        {
            entity.ToTable("dcb_tag_heads", table => table.HasCheckConstraint(
                "CK_TagHeads_Position_NotEmpty",
                "\"HeadPosition\" IS NULL OR length(\"HeadPosition\") > 0"));
            entity.HasKey(head => new { head.ServiceId, head.Tag });
            entity.Property(head => head.ServiceId).IsRequired().HasMaxLength(64);
            entity.Property(head => head.Tag).IsRequired();
            entity.Property(head => head.HeadPosition).HasMaxLength(100);
        });

        modelBuilder.Entity<DbTagHeadViolation>(entity =>
        {
            entity.ToTable("dcb_tag_head_violations", table => table.HasCheckConstraint(
                "CK_TagHeadViolations_Observed_NotEmpty",
                "length(\"ObservedPosition\") > 0"));
            entity.HasKey(violation => violation.Id);
            entity.Property(violation => violation.ServiceId).IsRequired().HasMaxLength(64);
            entity.Property(violation => violation.PreviousHeadPosition).IsRequired().HasMaxLength(100);
            entity.Property(violation => violation.ObservedPosition).IsRequired().HasMaxLength(100);
            entity.Property(violation => violation.DetectingWriter).IsRequired().HasMaxLength(128);
            entity.HasIndex(violation => new { violation.ServiceId, violation.Tag, violation.DetectedAtUtc })
                .HasDatabaseName("IX_TagHeadViolations_Service_Tag_Detected");
            // The prior-empty boolean gives Postgres a non-null durable identity component, so retries cannot create
            // duplicate audit records merely because a nullable unique-index component compares distinct.
            entity.HasIndex(violation => new
                {
                    violation.ServiceId,
                    violation.Tag,
                    violation.PreviousHeadWasEmpty,
                    violation.PreviousHeadPosition,
                    violation.ObservedPosition
                })
                .IsUnique()
                .HasDatabaseName("UX_TagHeadViolations_IdempotentRepair");
        });

        modelBuilder.Entity<DbTagHeadEnablementEpoch>(entity =>
        {
            entity.ToTable("dcb_tag_head_enablement_epochs");
            entity.HasKey(epoch => epoch.ServiceId);
            entity.Property(epoch => epoch.ServiceId).IsRequired().HasMaxLength(64);
        });
    }
}
