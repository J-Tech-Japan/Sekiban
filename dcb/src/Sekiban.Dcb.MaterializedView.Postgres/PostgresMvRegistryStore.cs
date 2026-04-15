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
                @LastUpdated,
                CAST(@Metadata AS JSONB)
            )
            ON CONFLICT (service_id, view_name, view_version, logical_table) DO UPDATE SET
                physical_table = EXCLUDED.physical_table,
                last_updated = EXCLUDED.last_updated,
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
        string serviceId,
        string viewName,
        int viewVersion,
        string sortableUniqueId,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE sekiban_mv_registry
            SET current_position = @SortableUniqueId,
                last_sortable_unique_id = @SortableUniqueId,
                last_updated = NOW()
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion;
            """;

        var parameters = new { ServiceId = serviceId, ViewName = viewName, ViewVersion = viewVersion, SortableUniqueId = sortableUniqueId };
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
            SELECT service_id,
                   view_name,
                   view_version,
                   logical_table,
                   physical_table,
                   status,
                   current_position,
                   target_position,
                   last_sortable_unique_id,
                   last_updated,
                   metadata
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
            SELECT service_id, view_name, active_version, activated_at
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
            row.LastUpdated,
            row.Metadata);

    private sealed record RegistryRow(
        string ServiceId,
        string ViewName,
        int ViewVersion,
        string LogicalTable,
        string PhysicalTable,
        string Status,
        string? CurrentPosition,
        string? TargetPosition,
        string? LastSortableUniqueId,
        DateTimeOffset LastUpdated,
        string? Metadata);

    private sealed record ActiveRow(string ServiceId, string ViewName, int ActiveVersion, DateTimeOffset ActivatedAt);
}
