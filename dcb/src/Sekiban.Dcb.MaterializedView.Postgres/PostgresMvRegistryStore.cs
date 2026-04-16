using System.Data;
using Dapper;
using Npgsql;

namespace Sekiban.Dcb.MaterializedView.Postgres;

public sealed class PostgresMvRegistryStore : IMvRegistryStore
{
    private readonly string _connectionString;

    public PostgresMvRegistryStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string registrySql = """
            CREATE TABLE IF NOT EXISTS sekiban_mv_registry (
                service_id TEXT NOT NULL,
                view_name TEXT NOT NULL,
                view_version INT NOT NULL,
                logical_table TEXT NOT NULL,
                physical_table TEXT NOT NULL,
                status TEXT NOT NULL,
                current_position TEXT NULL,
                target_position TEXT NULL,
                last_sortable_unique_id TEXT NULL,
                applied_event_version BIGINT NOT NULL DEFAULT 0,
                last_applied_source TEXT NULL,
                last_applied_at TIMESTAMPTZ NULL,
                last_stream_received_sortable_unique_id TEXT NULL,
                last_stream_received_at TIMESTAMPTZ NULL,
                last_stream_applied_sortable_unique_id TEXT NULL,
                last_catch_up_sortable_unique_id TEXT NULL,
                last_updated TIMESTAMPTZ NOT NULL,
                metadata JSONB NULL,
                PRIMARY KEY (service_id, view_name, view_version, logical_table)
            );
            """;

        const string activeSql = """
            CREATE TABLE IF NOT EXISTS sekiban_mv_active (
                service_id TEXT NOT NULL,
                view_name TEXT NOT NULL,
                active_version INT NOT NULL,
                activated_at TIMESTAMPTZ NOT NULL,
                PRIMARY KEY (service_id, view_name)
            );
            """;

        await connection.ExecuteAsync(new CommandDefinition(registrySql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(activeSql, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task RegisterAsync(MvRegistryEntry entry, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO sekiban_mv_registry (
                service_id,
                view_name,
                view_version,
                logical_table,
                physical_table,
                status,
                current_position,
                target_position,
                last_sortable_unique_id,
                applied_event_version,
                last_applied_source,
                last_applied_at,
                last_stream_received_sortable_unique_id,
                last_stream_received_at,
                last_stream_applied_sortable_unique_id,
                last_catch_up_sortable_unique_id,
                last_updated,
                metadata
            )
            VALUES (
                @ServiceId,
                @ViewName,
                @ViewVersion,
                @LogicalTable,
                @PhysicalTable,
                @Status,
                @CurrentPosition,
                @TargetPosition,
                @LastSortableUniqueId,
                @AppliedEventVersion,
                @LastAppliedSource,
                @LastAppliedAt,
                @LastStreamReceivedSortableUniqueId,
                @LastStreamReceivedAt,
                @LastStreamAppliedSortableUniqueId,
                @LastCatchUpSortableUniqueId,
                @LastUpdated,
                CAST(@Metadata AS JSONB)
            )
            ON CONFLICT (service_id, view_name, view_version, logical_table) DO UPDATE SET
                physical_table = EXCLUDED.physical_table,
                last_updated = EXCLUDED.last_updated,
                applied_event_version = sekiban_mv_registry.applied_event_version,
                last_applied_source = COALESCE(sekiban_mv_registry.last_applied_source, EXCLUDED.last_applied_source),
                last_applied_at = COALESCE(sekiban_mv_registry.last_applied_at, EXCLUDED.last_applied_at),
                last_stream_received_sortable_unique_id = COALESCE(sekiban_mv_registry.last_stream_received_sortable_unique_id, EXCLUDED.last_stream_received_sortable_unique_id),
                last_stream_received_at = COALESCE(sekiban_mv_registry.last_stream_received_at, EXCLUDED.last_stream_received_at),
                last_stream_applied_sortable_unique_id = COALESCE(sekiban_mv_registry.last_stream_applied_sortable_unique_id, EXCLUDED.last_stream_applied_sortable_unique_id),
                last_catch_up_sortable_unique_id = COALESCE(sekiban_mv_registry.last_catch_up_sortable_unique_id, EXCLUDED.last_catch_up_sortable_unique_id),
                metadata = COALESCE(EXCLUDED.metadata, sekiban_mv_registry.metadata);
            """;

        var parameters = new
        {
            entry.ServiceId,
            entry.ViewName,
            entry.ViewVersion,
            entry.LogicalTable,
            entry.PhysicalTable,
            Status = entry.Status.ToString().ToLowerInvariant(),
            entry.CurrentPosition,
            entry.TargetPosition,
            entry.LastSortableUniqueId,
            entry.AppliedEventVersion,
            entry.LastAppliedSource,
            entry.LastAppliedAt,
            entry.LastStreamReceivedSortableUniqueId,
            entry.LastStreamReceivedAt,
            entry.LastStreamAppliedSortableUniqueId,
            entry.LastCatchUpSortableUniqueId,
            entry.LastUpdated,
            entry.Metadata
        };

        if (transaction is null)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
            return;
        }

        await transaction.Connection!.ExecuteAsync(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task UpdatePositionAsync(
        MvPositionUpdate update,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE sekiban_mv_registry
            SET current_position = @SortableUniqueId,
                last_sortable_unique_id = @SortableUniqueId,
                applied_event_version = applied_event_version + @AppliedEventVersionDelta,
                last_applied_source = @Source,
                last_applied_at = NOW(),
                last_stream_applied_sortable_unique_id = CASE
                    WHEN @Source = 'stream' THEN @SortableUniqueId
                    ELSE last_stream_applied_sortable_unique_id
                END,
                last_catch_up_sortable_unique_id = CASE
                    WHEN @Source = 'catchup' THEN @SortableUniqueId
                    ELSE last_catch_up_sortable_unique_id
                END,
                last_updated = NOW()
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion;
            """;

        var parameters = new
        {
            update.ServiceId,
            update.ViewName,
            update.ViewVersion,
            update.SortableUniqueId,
            update.AppliedEventVersionDelta,
            Source = update.Source == MvApplySource.Stream ? "stream" : "catchup"
        };
        await ExecuteAsync(sql, parameters, transaction, cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkStreamReceivedAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        string sortableUniqueId,
        DateTimeOffset receivedAt,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE sekiban_mv_registry
            SET last_stream_received_sortable_unique_id = CASE
                    WHEN last_stream_received_sortable_unique_id IS NULL
                      OR last_stream_received_sortable_unique_id < @SortableUniqueId THEN @SortableUniqueId
                    ELSE last_stream_received_sortable_unique_id
                END,
                last_stream_received_at = @ReceivedAt,
                last_updated = NOW()
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion;
            """;

        var parameters = new
        {
            ServiceId = serviceId,
            ViewName = viewName,
            ViewVersion = viewVersion,
            SortableUniqueId = sortableUniqueId,
            ReceivedAt = receivedAt
        };
        await ExecuteAsync(sql, parameters, transaction, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateStatusAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        MvStatus status,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE sekiban_mv_registry
            SET status = @Status,
                last_updated = NOW()
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion;
            """;

        var parameters = new { ServiceId = serviceId, ViewName = viewName, ViewVersion = viewVersion, Status = status.ToString().ToLowerInvariant() };
        await ExecuteAsync(sql, parameters, transaction, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MvRegistryEntry>> GetEntriesAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT service_id AS ServiceId,
                   view_name AS ViewName,
                   view_version AS ViewVersion,
                   logical_table AS LogicalTable,
                   physical_table AS PhysicalTable,
                   status AS Status,
                   current_position AS CurrentPosition,
                   target_position AS TargetPosition,
                   last_sortable_unique_id AS LastSortableUniqueId,
                   applied_event_version AS AppliedEventVersion,
                   last_applied_source AS LastAppliedSource,
                   last_applied_at AS LastAppliedAt,
                   last_stream_received_sortable_unique_id AS LastStreamReceivedSortableUniqueId,
                   last_stream_received_at AS LastStreamReceivedAt,
                   last_stream_applied_sortable_unique_id AS LastStreamAppliedSortableUniqueId,
                   last_catch_up_sortable_unique_id AS LastCatchUpSortableUniqueId,
                   last_updated AS LastUpdated,
                   metadata::text AS Metadata
            FROM sekiban_mv_registry
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion
            ORDER BY logical_table;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<RegistryRow>(
            new CommandDefinition(sql, new { ServiceId = serviceId, ViewName = viewName, ViewVersion = viewVersion }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        return rows.Select(MapEntry).ToList();
    }

    public async Task<MvActiveEntry?> GetActiveAsync(
        string serviceId,
        string viewName,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT service_id AS ServiceId,
                   view_name AS ViewName,
                   active_version AS ActiveVersion,
                   activated_at AS ActivatedAt
            FROM sekiban_mv_active
            WHERE service_id = @ServiceId
              AND view_name = @ViewName;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<ActiveRow>(
            new CommandDefinition(sql, new { ServiceId = serviceId, ViewName = viewName }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        return row is null ? null : new MvActiveEntry(row.ServiceId, row.ViewName, row.ActiveVersion, row.ActivatedAt);
    }

    public async Task SetActiveAsync(
        string serviceId,
        string viewName,
        int activeVersion,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO sekiban_mv_active (service_id, view_name, active_version, activated_at)
            VALUES (@ServiceId, @ViewName, @ActiveVersion, NOW())
            ON CONFLICT (service_id, view_name) DO UPDATE SET
                active_version = EXCLUDED.active_version,
                activated_at = EXCLUDED.activated_at;
            """;

        var parameters = new { ServiceId = serviceId, ViewName = viewName, ActiveVersion = activeVersion };
        await ExecuteAsync(sql, parameters, transaction, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(
        string sql,
        object parameters,
        IDbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
            return;
        }

        await transaction.Connection!.ExecuteAsync(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static MvRegistryEntry MapEntry(RegistryRow row) =>
        new(
            row.ServiceId,
            row.ViewName,
            row.ViewVersion,
            row.LogicalTable,
            row.PhysicalTable,
            Enum.Parse<MvStatus>(row.Status, ignoreCase: true),
            row.CurrentPosition,
            row.TargetPosition,
            row.LastSortableUniqueId,
            row.AppliedEventVersion,
            row.LastAppliedSource,
            row.LastAppliedAt,
            row.LastStreamReceivedSortableUniqueId,
            row.LastStreamReceivedAt,
            row.LastStreamAppliedSortableUniqueId,
            row.LastCatchUpSortableUniqueId,
            row.LastUpdated,
            row.Metadata);

    private sealed class RegistryRow
    {
        public RegistryRow(
            string serviceId,
            string viewName,
            int viewVersion,
            string logicalTable,
            string physicalTable,
            string status,
            string? currentPosition,
            string? targetPosition,
            string? lastSortableUniqueId,
            long appliedEventVersion,
            string? lastAppliedSource,
            DateTime? lastAppliedAt,
            string? lastStreamReceivedSortableUniqueId,
            DateTime? lastStreamReceivedAt,
            string? lastStreamAppliedSortableUniqueId,
            string? lastCatchUpSortableUniqueId,
            DateTime lastUpdated,
            string? metadata)
        {
            ServiceId = serviceId;
            ViewName = viewName;
            ViewVersion = viewVersion;
            LogicalTable = logicalTable;
            PhysicalTable = physicalTable;
            Status = status;
            CurrentPosition = currentPosition;
            TargetPosition = targetPosition;
            LastSortableUniqueId = lastSortableUniqueId;
            AppliedEventVersion = appliedEventVersion;
            LastAppliedSource = lastAppliedSource;
            LastAppliedAt = lastAppliedAt is null ? null : Normalize(lastAppliedAt.Value);
            LastStreamReceivedSortableUniqueId = lastStreamReceivedSortableUniqueId;
            LastStreamReceivedAt = lastStreamReceivedAt is null ? null : Normalize(lastStreamReceivedAt.Value);
            LastStreamAppliedSortableUniqueId = lastStreamAppliedSortableUniqueId;
            LastCatchUpSortableUniqueId = lastCatchUpSortableUniqueId;
            LastUpdated = Normalize(lastUpdated);
            Metadata = metadata;
        }

        public string ServiceId { get; }
        public string ViewName { get; }
        public int ViewVersion { get; }
        public string LogicalTable { get; }
        public string PhysicalTable { get; }
        public string Status { get; }
        public string? CurrentPosition { get; }
        public string? TargetPosition { get; }
        public string? LastSortableUniqueId { get; }
        public long AppliedEventVersion { get; }
        public string? LastAppliedSource { get; }
        public DateTimeOffset? LastAppliedAt { get; }
        public string? LastStreamReceivedSortableUniqueId { get; }
        public DateTimeOffset? LastStreamReceivedAt { get; }
        public string? LastStreamAppliedSortableUniqueId { get; }
        public string? LastCatchUpSortableUniqueId { get; }
        public DateTimeOffset LastUpdated { get; }
        public string? Metadata { get; }
    }

    private sealed class ActiveRow
    {
        public ActiveRow(
            string serviceId,
            string viewName,
            int activeVersion,
            DateTime activatedAt)
        {
            ServiceId = serviceId;
            ViewName = viewName;
            ActiveVersion = activeVersion;
            ActivatedAt = Normalize(activatedAt);
        }

        public string ServiceId { get; }
        public string ViewName { get; }
        public int ActiveVersion { get; }
        public DateTimeOffset ActivatedAt { get; }
    }

    private static DateTimeOffset Normalize(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
