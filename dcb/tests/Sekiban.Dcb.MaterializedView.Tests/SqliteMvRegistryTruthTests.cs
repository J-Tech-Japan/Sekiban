using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Tests;

public sealed class SqliteMvRegistryTruthTests
{
    [Fact]
    public async Task NewRows_PersistUnknownKnownZeroAndKnownPositionWithoutLosingLegacyFields()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"sekiban-mv-truth-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteMvRegistryStore($"Data Source={databasePath}");
            await store.EnsureInfrastructureAsync();

            await store.RegisterAsync(CreateEntry("orders", "orders_table"));
            var initial = Assert.Single(await store.GetEntriesAsync("truth-service", "Truth", 1));
            Assert.True(initial.CurrentCheckpointTruth.IsUnknown);
            Assert.Equal(MvCheckpointUnknownReason.NotObserved, initial.CurrentCheckpointTruth.UnknownReason);

            await store.UpdatePositionAsync(
                new MvPositionUpdate(
                    "truth-service",
                    "Truth",
                    1,
                    SortableUniqueId.MinValue.Value,
                    MvApplySource.CatchUp,
                    AppliedEventVersionDelta: 0)
                {
                    CheckpointTruth = MvCheckpointTruth.KnownZero()
                });
            var knownZero = Assert.Single(await store.GetEntriesAsync("truth-service", "Truth", 1));
            Assert.True(knownZero.CurrentCheckpointTruth.IsKnownZero);
            Assert.Equal(SortableUniqueId.MinValue.Value, knownZero.CurrentPosition);

            var position = SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-1), Guid.NewGuid());
            await store.UpdatePositionAsync(
                new MvPositionUpdate("truth-service", "Truth", 1, position, MvApplySource.Stream));
            var known = Assert.Single(await store.GetEntriesAsync("truth-service", "Truth", 1));
            Assert.True(known.CurrentCheckpointTruth.IsKnown);
            Assert.False(known.CurrentCheckpointTruth.IsKnownZero);
            Assert.Equal(position, known.CurrentCheckpointTruth.PositionValue);
            Assert.Equal(position, known.CurrentPosition);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task ExistingLegacySchema_IsMigratedAndNullCheckpointRemainsUnknown()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"sekiban-mv-legacy-{Guid.NewGuid():N}.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                await connection.ExecuteAsync(
                    """
                    CREATE TABLE sekiban_mv_registry (
                        service_id TEXT NOT NULL,
                        view_name TEXT NOT NULL,
                        view_version INTEGER NOT NULL,
                        logical_table TEXT NOT NULL,
                        physical_table TEXT NOT NULL,
                        status TEXT NOT NULL,
                        current_position TEXT NULL,
                        target_position TEXT NULL,
                        last_sortable_unique_id TEXT NULL,
                        applied_event_version INTEGER NOT NULL DEFAULT 0,
                        last_applied_source TEXT NULL,
                        last_applied_at TEXT NULL,
                        last_stream_received_sortable_unique_id TEXT NULL,
                        last_stream_received_at TEXT NULL,
                        last_stream_applied_sortable_unique_id TEXT NULL,
                        last_catch_up_sortable_unique_id TEXT NULL,
                        last_updated TEXT NOT NULL,
                        metadata TEXT NULL,
                        PRIMARY KEY (service_id, view_name, view_version, logical_table)
                    );
                    INSERT INTO sekiban_mv_registry (
                        service_id, view_name, view_version, logical_table, physical_table, status, last_updated)
                    VALUES ('truth-service', 'Truth', 1, 'orders', 'orders_table', 'ready', @LastUpdated);
                    """,
                    new { LastUpdated = DateTimeOffset.UtcNow.ToString("O") });
            }

            var store = new SqliteMvRegistryStore($"Data Source={databasePath}");
            await store.EnsureInfrastructureAsync();
            await using var migratedConnection = new SqliteConnection($"Data Source={databasePath}");
            await migratedConnection.OpenAsync();
            var columns = (await migratedConnection.QueryAsync<string>(
                    "SELECT name FROM pragma_table_info('sekiban_mv_registry');"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("current_checkpoint_truth", columns);
            Assert.Contains("target_checkpoint_truth", columns);

            var entry = Assert.Single(await store.GetEntriesAsync("truth-service", "Truth", 1));
            Assert.True(entry.CurrentCheckpointTruth.IsUnknown);
            Assert.Equal(MvCheckpointUnknownReason.LegacyNull, entry.CurrentCheckpointTruth.UnknownReason);
            Assert.Null(entry.CurrentPosition);

            var legacyPosition = SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-2), Guid.NewGuid());
            await migratedConnection.ExecuteAsync(
                "UPDATE sekiban_mv_registry SET current_position = @LegacyPosition WHERE service_id = 'truth-service';",
                new { LegacyPosition = legacyPosition });
            // Initialization re-registers an existing legacy row. It must not
            // turn the legacy position into authoritative truth or prevent a
            // subsequent empty-history proof from becoming Known-zero.
            await store.RegisterAsync(CreateEntry("truth-service", "orders_table"));
            await store.UpdatePositionAsync(
                new MvPositionUpdate(
                    "truth-service",
                    "Truth",
                    1,
                    SortableUniqueId.MinValue.Value,
                    MvApplySource.CatchUp,
                    AppliedEventVersionDelta: 0)
                {
                    CheckpointTruth = MvCheckpointTruth.KnownZero()
                });

            var rebuilt = Assert.Single(await store.GetEntriesAsync("truth-service", "Truth", 1));
            Assert.True(rebuilt.CurrentCheckpointTruth.IsKnownZero);
            Assert.Equal(SortableUniqueId.MinValue.Value, rebuilt.CurrentPosition);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static MvRegistryEntry CreateEntry(string serviceId, string physicalTable) =>
        new()
        {
            ServiceId = serviceId == "orders" ? "truth-service" : serviceId,
            ViewName = "Truth",
            ViewVersion = 1,
            LogicalTable = "orders",
            PhysicalTable = physicalTable,
            Status = MvStatus.CatchingUp,
            LastUpdated = DateTimeOffset.UtcNow
        };
}
