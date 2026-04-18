using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Sekiban.Dcb.MaterializedView.Sqlite;

public sealed class SqliteMvRegistryStore : IMvRegistryStore
{
    private readonly string _connectionString;

    public SqliteMvRegistryStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string pragmaSql = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            """;

        const string registrySql = """
            CREATE TABLE IF NOT EXISTS sekiban_mv_registry (
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
            """;

        const string activeSql = """
            CREATE TABLE IF NOT EXISTS sekiban_mv_active (
                service_id TEXT NOT NULL,
                view_name TEXT NOT NULL,
                active_version INTEGER NOT NULL,
                activated_at TEXT NOT NULL,
                PRIMARY KEY (service_id, view_name)
            );
            """;

        await connection.ExecuteAsync(new CommandDefinition(pragmaSql, cancellationToken: cancellationToken)).ConfigureAwait(false);
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
                @Metadata
            )
            ON CONFLICT (service_id, view_name, view_version, logical_table) DO UPDATE SET
                physical_table = excluded.physical_table,
                last_updated = excluded.last_updated,
                applied_event_version = sekiban_mv_registry.applied_event_version,
                last_applied_source = COALESCE(sekiban_mv_registry.last_applied_source, excluded.last_applied_source),
                last_applied_at = COALESCE(sekiban_mv_registry.last_applied_at, excluded.last_applied_at),
                last_stream_received_sortable_unique_id = COALESCE(sekiban_mv_registry.last_stream_received_sortable_unique_id, excluded.last_stream_received_sortable_unique_id),
                last_stream_received_at = COALESCE(sekiban_mv_registry.last_stream_received_at, excluded.last_stream_received_at),
                last_stream_applied_sortable_unique_id = COALESCE(sekiban_mv_registry.last_stream_applied_sortable_unique_id, excluded.last_stream_applied_sortable_unique_id),
                last_catch_up_sortable_unique_id = COALESCE(sekiban_mv_registry.last_catch_up_sortable_unique_id, excluded.last_catch_up_sortable_unique_id),
                metadata = COALESCE(excluded.metadata, sekiban_mv_registry.metadata);
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
            LastAppliedAt = SerializeDate(entry.LastAppliedAt),
            entry.LastStreamReceivedSortableUniqueId,
            LastStreamReceivedAt = SerializeDate(entry.LastStreamReceivedAt),
            entry.LastStreamAppliedSortableUniqueId,
            entry.LastCatchUpSortableUniqueId,
            LastUpdated = SerializeDate(entry.LastUpdated),
            entry.Metadata
        };

        await ExecuteAsync(sql, parameters, transaction, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdatePositionAsync(
        MvPositionUpdate update,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE sekiban_mv_registry
            SET current_position = CASE
                    WHEN current_position IS NULL
                      OR current_position < @SortableUniqueId THEN @SortableUniqueId
                    ELSE current_position
                END,
                last_sortable_unique_id = CASE
                    WHEN last_sortable_unique_id IS NULL
                      OR last_sortable_unique_id < @SortableUniqueId THEN @SortableUniqueId
                    ELSE last_sortable_unique_id
                END,
                applied_event_version = applied_event_version + @AppliedEventVersionDelta,
                last_applied_source = @Source,
                last_applied_at = @Now,
                last_stream_applied_sortable_unique_id = CASE
                    WHEN @Source = 'stream'
                      AND (last_stream_applied_sortable_unique_id IS NULL
                        OR last_stream_applied_sortable_unique_id < @SortableUniqueId) THEN @SortableUniqueId
                    ELSE last_stream_applied_sortable_unique_id
                END,
                last_catch_up_sortable_unique_id = CASE
                    WHEN @Source = 'catchup'
                      AND (last_catch_up_sortable_unique_id IS NULL
                        OR last_catch_up_sortable_unique_id < @SortableUniqueId) THEN @SortableUniqueId
                    ELSE last_catch_up_sortable_unique_id
                END,
                last_updated = @Now
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion;
            """;

        await ExecuteAsync(
            sql,
            new
            {
                update.ServiceId,
                update.ViewName,
                update.ViewVersion,
                update.SortableUniqueId,
                update.AppliedEventVersionDelta,
                Source = update.Source == MvApplySource.Stream ? "stream" : "catchup",
                Now = SerializeDate(DateTimeOffset.UtcNow)
            },
            transaction,
            cancellationToken).ConfigureAwait(false);
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
                last_updated = @Now
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion;
            """;

        await ExecuteAsync(
            sql,
            new
            {
                ServiceId = serviceId,
                ViewName = viewName,
                ViewVersion = viewVersion,
                SortableUniqueId = sortableUniqueId,
                ReceivedAt = SerializeDate(receivedAt),
                Now = SerializeDate(DateTimeOffset.UtcNow)
            },
            transaction,
            cancellationToken).ConfigureAwait(false);
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
                last_updated = @Now
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion;
            """;

        await ExecuteAsync(
            sql,
            new
            {
                ServiceId = serviceId,
                ViewName = viewName,
                ViewVersion = viewVersion,
                Status = status.ToString().ToLowerInvariant(),
                Now = SerializeDate(DateTimeOffset.UtcNow)
            },
            transaction,
            cancellationToken).ConfigureAwait(false);
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
                   metadata AS Metadata
            FROM sekiban_mv_registry
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion
            ORDER BY logical_table;
            """;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync(
            new CommandDefinition(sql, new { ServiceId = serviceId, ViewName = viewName, ViewVersion = viewVersion }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        return rows.Select(row => (MvRegistryEntry)MapEntry(ToDictionary(row))).ToList();
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

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { ServiceId = serviceId, ViewName = viewName }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        return row is null ? null : MapActiveEntry(ToDictionary(row));
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
            VALUES (@ServiceId, @ViewName, @ActiveVersion, @ActivatedAt)
            ON CONFLICT (service_id, view_name) DO UPDATE SET
                active_version = excluded.active_version,
                activated_at = excluded.activated_at;
            """;

        await ExecuteAsync(
            sql,
            new
            {
                ServiceId = serviceId,
                ViewName = viewName,
                ActiveVersion = activeVersion,
                ActivatedAt = SerializeDate(DateTimeOffset.UtcNow)
            },
            transaction,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(
        string sql,
        object parameters,
        IDbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
            return;
        }

        await transaction.Connection!.ExecuteAsync(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static MvRegistryEntry MapEntry(IReadOnlyDictionary<string, object?> row) =>
        new()
        {
            ServiceId = ReadRequiredString(row, "ServiceId"),
            ViewName = ReadRequiredString(row, "ViewName"),
            ViewVersion = ReadRequiredInt(row, "ViewVersion"),
            LogicalTable = ReadRequiredString(row, "LogicalTable"),
            PhysicalTable = ReadRequiredString(row, "PhysicalTable"),
            Status = Enum.Parse<MvStatus>(ReadRequiredString(row, "Status"), ignoreCase: true),
            CurrentPosition = ReadNullableString(row, "CurrentPosition"),
            TargetPosition = ReadNullableString(row, "TargetPosition"),
            LastSortableUniqueId = ReadNullableString(row, "LastSortableUniqueId"),
            AppliedEventVersion = ReadRequiredLong(row, "AppliedEventVersion"),
            LastAppliedSource = ReadNullableString(row, "LastAppliedSource"),
            LastAppliedAt = ReadNullableDateTimeOffset(row, "LastAppliedAt"),
            LastStreamReceivedSortableUniqueId = ReadNullableString(row, "LastStreamReceivedSortableUniqueId"),
            LastStreamReceivedAt = ReadNullableDateTimeOffset(row, "LastStreamReceivedAt"),
            LastStreamAppliedSortableUniqueId = ReadNullableString(row, "LastStreamAppliedSortableUniqueId"),
            LastCatchUpSortableUniqueId = ReadNullableString(row, "LastCatchUpSortableUniqueId"),
            LastUpdated = ReadRequiredDateTimeOffset(row, "LastUpdated"),
            Metadata = ReadNullableString(row, "Metadata")
        };

    private static MvActiveEntry MapActiveEntry(IReadOnlyDictionary<string, object?> row) =>
        new(
            ReadRequiredString(row, "ServiceId"),
            ReadRequiredString(row, "ViewName"),
            ReadRequiredInt(row, "ActiveVersion"),
            ReadRequiredDateTimeOffset(row, "ActivatedAt"));

    private static IReadOnlyDictionary<string, object?> ToDictionary(object row)
    {
        if (row is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            return readOnlyDictionary;
        }

        if (row is IDictionary<string, object?> dictionary)
        {
            return new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase);
        }

        if (row is IDictionary<string, object> nonNullableDictionary)
        {
            return nonNullableDictionary
                .Select(pair => new KeyValuePair<string, object?>(pair.Key, pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        if (row is System.Collections.IDictionary legacyDictionary)
        {
            return legacyDictionary.Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(
                    entry => entry.Key.ToString() ?? string.Empty,
                    entry => entry.Value,
                    StringComparer.OrdinalIgnoreCase);
        }

        return row.GetType()
            .GetProperties()
            .ToDictionary(property => property.Name, property => property.GetValue(row), StringComparer.OrdinalIgnoreCase);
    }

    private static string ReadRequiredString(IReadOnlyDictionary<string, object?> row, string key) =>
        TryGetValue(row, key, out var value) && value is not null
            ? value.ToString()!
            : throw new InvalidOperationException($"Registry row is missing required value '{key}'.");

    private static string? ReadNullableString(IReadOnlyDictionary<string, object?> row, string key) =>
        TryGetValue(row, key, out var value) && value is not null
            ? value.ToString()
            : null;

    private static int ReadRequiredInt(IReadOnlyDictionary<string, object?> row, string key) =>
        Convert.ToInt32(TryGetValue(row, key, out var value)
            ? value
            : throw new InvalidOperationException($"Registry row is missing required value '{key}'."));

    private static long ReadRequiredLong(IReadOnlyDictionary<string, object?> row, string key) =>
        Convert.ToInt64(TryGetValue(row, key, out var value)
            ? value
            : throw new InvalidOperationException($"Registry row is missing required value '{key}'."));

    private static DateTimeOffset ReadRequiredDateTimeOffset(IReadOnlyDictionary<string, object?> row, string key) =>
        ReadDateTimeOffsetCore(
            TryGetValue(row, key, out var value)
                ? value
                : throw new InvalidOperationException($"Registry row is missing required value '{key}'."),
            key) ??
        throw new InvalidOperationException($"Registry row is missing required timestamp '{key}'.");

    private static DateTimeOffset? ReadNullableDateTimeOffset(IReadOnlyDictionary<string, object?> row, string key) =>
        TryGetValue(row, key, out var value) ? ReadDateTimeOffsetCore(value, key) : null;

    private static bool TryGetValue(IReadOnlyDictionary<string, object?> row, string key, out object? value)
    {
        if (row.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var pair in row)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static DateTimeOffset? ReadDateTimeOffsetCore(object? value, string key) =>
        value switch
        {
            null or DBNull => null,
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => Normalize(dateTime),
            string text when DateTimeOffset.TryParse(text, out var parsed) => parsed,
            _ => throw new InvalidOperationException($"Registry row value '{key}' must be a timestamp.")
        };

    private static DateTimeOffset Normalize(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string? SerializeDate(DateTimeOffset? value) =>
        value?.UtcDateTime.ToString("O");
}
