using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.MaterializedView.MySql;

// ============================================================================
// Unsafe Window Materialized View — MySQL runtime (issue #1035).
//
// Dialect notes:
//   - VARCHAR(191) for _projection_key PK so utf8mb4 indexes stay within the
//     767/3072-byte InnoDB key limit regardless of server configuration.
//   - BOOLEAN (TINYINT(1)) / DATETIME(6) / VARCHAR(64) / JSON-free plain types.
//   - ON DUPLICATE KEY UPDATE for upserts.
//   - FOR UPDATE / FOR UPDATE SKIP LOCKED (MySQL 8.0+ is required).
//   - information_schema.columns with table_schema = DATABASE() for validation.
// ============================================================================

public sealed class UnsafeWindowMvMySqlSchemaResolver
{
    // MySQL identifier limit is 64 chars, and InnoDB table name internally
    // reserves a little headroom. We pick 63 (same as Postgres) so projector
    // definitions that validated on Postgres keep working here.
    private const int MaxSharedPrefixLength = 63 - 13;

    public UnsafeWindowMvMySqlSchemaResolver(string viewName, int viewVersion, UnsafeWindowMvSchema schema)
    {
        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name must be non-empty.", nameof(viewName));
        }
        if (viewVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewVersion), "View version must be positive.");
        }
        if (schema.Columns.Count == 0)
        {
            throw new ArgumentException("Unsafe window MV schema must declare at least one business column.", nameof(schema));
        }

        var reserved = new HashSet<string>(UnsafeWindowMvReservedColumns.AllReservedNames, StringComparer.OrdinalIgnoreCase);
        foreach (var column in schema.Columns)
        {
            if (reserved.Contains(column.Name))
            {
                throw new InvalidOperationException(
                    $"Unsafe window MV '{viewName}' declares a business column '{column.Name}' that clashes with a framework-managed metadata column. Rename the business column.");
            }

            MvPhysicalName.ValidateIdentifier(column.Name);
            ValidateSqlType(column.Name, column.SqlType);
        }

        var sanitized = MvPhysicalName.SanitizeSegment(viewName);
        var rawPrefix = $"sekiban_uwmv_{sanitized}_v{viewVersion}";
        var prefix = rawPrefix.Length <= MaxSharedPrefixLength
            ? rawPrefix
            : BuildBoundedPrefix(sanitized, viewVersion);

        SafeTable = $"{prefix}_safe";
        UnsafeTable = $"{prefix}_unsafe";
        CurrentView = $"{prefix}_current";
        CurrentLiveView = $"{prefix}_current_live";

        foreach (var identifier in new[] { SafeTable, UnsafeTable, CurrentView, CurrentLiveView })
        {
            MvPhysicalName.ValidateIdentifier(identifier);
        }

        Schema = schema;
        ViewName = viewName;
        ViewVersion = viewVersion;
    }

    public string ViewName { get; }
    public int ViewVersion { get; }
    public string SafeTable { get; }
    public string UnsafeTable { get; }
    public string CurrentView { get; }
    public string CurrentLiveView { get; }
    public UnsafeWindowMvSchema Schema { get; }

    private static string BuildBoundedPrefix(string sanitized, int viewVersion)
    {
        var versionSuffix = $"_v{viewVersion}";
        var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(sanitized)))
            .Substring(0, 8)
            .ToLowerInvariant();

        var headroom = MaxSharedPrefixLength - ("sekiban_uwmv__".Length + hash.Length + versionSuffix.Length);
        if (headroom < 1)
        {
            headroom = 1;
        }
        var head = sanitized.Substring(0, Math.Min(headroom, sanitized.Length));
        return $"sekiban_uwmv_{head}_{hash}{versionSuffix}";
    }

    private static void ValidateSqlType(string columnName, string sqlType)
    {
        if (string.IsNullOrWhiteSpace(sqlType))
        {
            throw new ArgumentException($"Business column '{columnName}' has an empty SQL type.");
        }
        foreach (var ch in sqlType)
        {
            if (ch == ';' || ch == '\r' || ch == '\n')
            {
                throw new ArgumentException(
                    $"Business column '{columnName}' declares a SQL type containing illegal characters (semicolon / newline). Type: '{sqlType}'.");
            }
        }
        if (sqlType.Contains("--", StringComparison.Ordinal) || sqlType.Contains("/*", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Business column '{columnName}' declares a SQL type containing SQL comment markers. Type: '{sqlType}'.");
        }
    }
}

internal static class UnsafeWindowMvMySqlTypes
{
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["TEXT"] = "TEXT",
        ["BOOLEAN"] = "TINYINT(1)",
        ["BOOL"] = "TINYINT(1)",
        ["UUID"] = "CHAR(36)",
        ["TIMESTAMPTZ"] = "DATETIME(6)",
        ["TIMESTAMP"] = "DATETIME(6)",
        ["BIGINT"] = "BIGINT",
        ["INT"] = "INT",
        ["INTEGER"] = "INT",
        ["DATE"] = "DATE",
        ["REAL"] = "FLOAT",
        ["DOUBLE"] = "DOUBLE",
        ["SMALLINT"] = "SMALLINT"
    };

    public static string Translate(string declaredType) =>
        UnsafeWindowMvLogicalTypes.Translate(declaredType, Map);
}

internal static class UnsafeWindowMvMySqlDdl
{
    public static IReadOnlyList<string> BuildInitializeStatements(UnsafeWindowMvMySqlSchemaResolver resolver)
    {
        var businessDdl = string.Join(
            ",\n    ",
            resolver.Schema.Columns.Select(c => $"{c.Name} {UnsafeWindowMvMySqlTypes.Translate(c.SqlType)}"));

        // VARCHAR(191) lets utf8mb4 indexes fit within 767 bytes on older
        // InnoDB configurations too. Modern MySQL 8.0+ supports longer keys
        // but we stay on the safe floor for compatibility.
        var safeTable = $"""
            CREATE TABLE IF NOT EXISTS {resolver.SafeTable} (
                {UnsafeWindowMvReservedColumns.ProjectionKey} VARCHAR(191) NOT NULL,
                {businessDdl},
                {UnsafeWindowMvReservedColumns.IsDeleted} TINYINT(1) NOT NULL DEFAULT 0,
                {UnsafeWindowMvReservedColumns.LastSortableUniqueId} VARCHAR(64) NOT NULL,
                {UnsafeWindowMvReservedColumns.LastEventVersion} BIGINT NOT NULL DEFAULT 0,
                {UnsafeWindowMvReservedColumns.LastAppliedAt} DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                {UnsafeWindowMvReservedColumns.SafeConfirmedAt} DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                PRIMARY KEY ({UnsafeWindowMvReservedColumns.ProjectionKey})
            );
            """;

        // MySQL identifiers are capped at 64 chars. The obvious index name
        // "idx_<unsafe-table>_safe_due_at" can overflow once the view name is
        // more than a few characters long, so we use a shorter suffix form.
        var unsafeTable = $"""
            CREATE TABLE IF NOT EXISTS {resolver.UnsafeTable} (
                {UnsafeWindowMvReservedColumns.ProjectionKey} VARCHAR(191) NOT NULL,
                {businessDdl},
                {UnsafeWindowMvReservedColumns.IsDeleted} TINYINT(1) NOT NULL DEFAULT 0,
                {UnsafeWindowMvReservedColumns.LastSortableUniqueId} VARCHAR(64) NOT NULL,
                {UnsafeWindowMvReservedColumns.LastEventVersion} BIGINT NOT NULL DEFAULT 0,
                {UnsafeWindowMvReservedColumns.LastAppliedAt} DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                {UnsafeWindowMvReservedColumns.UnsafeSince} DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                {UnsafeWindowMvReservedColumns.SafeDueAt} DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                {UnsafeWindowMvReservedColumns.NeedsRebuild} TINYINT(1) NOT NULL DEFAULT 0,
                PRIMARY KEY ({UnsafeWindowMvReservedColumns.ProjectionKey}),
                INDEX {resolver.UnsafeTable}_sd ({UnsafeWindowMvReservedColumns.SafeDueAt})
            );
            """;

        // CREATE OR REPLACE VIEW is supported by MySQL 5.x+.
        var currentView = $"""
            CREATE OR REPLACE VIEW {resolver.CurrentView} AS
            SELECT
                u.{UnsafeWindowMvReservedColumns.ProjectionKey},
                {string.Join(", ", resolver.Schema.Columns.Select(c => $"u.{c.Name}"))},
                u.{UnsafeWindowMvReservedColumns.IsDeleted},
                u.{UnsafeWindowMvReservedColumns.LastSortableUniqueId},
                u.{UnsafeWindowMvReservedColumns.LastEventVersion},
                u.{UnsafeWindowMvReservedColumns.LastAppliedAt}
            FROM {resolver.UnsafeTable} u
            UNION ALL
            SELECT
                s.{UnsafeWindowMvReservedColumns.ProjectionKey},
                {string.Join(", ", resolver.Schema.Columns.Select(c => $"s.{c.Name}"))},
                s.{UnsafeWindowMvReservedColumns.IsDeleted},
                s.{UnsafeWindowMvReservedColumns.LastSortableUniqueId},
                s.{UnsafeWindowMvReservedColumns.LastEventVersion},
                s.{UnsafeWindowMvReservedColumns.LastAppliedAt}
            FROM {resolver.SafeTable} s
            WHERE NOT EXISTS (
                SELECT 1 FROM {resolver.UnsafeTable} u2
                WHERE u2.{UnsafeWindowMvReservedColumns.ProjectionKey} = s.{UnsafeWindowMvReservedColumns.ProjectionKey}
            )
            """;

        var currentLiveView = $"""
            CREATE OR REPLACE VIEW {resolver.CurrentLiveView} AS
            SELECT *
            FROM {resolver.CurrentView}
            WHERE {UnsafeWindowMvReservedColumns.IsDeleted} = 0
            """;

        return [safeTable, unsafeTable, currentView, currentLiveView];
    }
}

internal static class UnsafeWindowMvMySqlValidator
{
    public static async Task ValidateAsync(
        UnsafeWindowMvMySqlSchemaResolver resolver,
        MySqlConnection connection,
        CancellationToken ct)
    {
        foreach (var table in new[] { resolver.SafeTable, resolver.UnsafeTable })
        {
            var columns = (await connection.QueryAsync<(string column_name, string data_type)>(
                new CommandDefinition(
                    """
                    SELECT column_name, data_type
                    FROM information_schema.columns
                    WHERE table_schema = DATABASE()
                      AND table_name = @Table
                    """,
                    new { Table = table },
                    cancellationToken: ct))).ToList();

            if (columns.Count == 0)
            {
                throw new InvalidOperationException($"Unsafe window MV table '{table}' was not created by initialization.");
            }

            var columnSet = new HashSet<string>(columns.Select(c => c.column_name), StringComparer.OrdinalIgnoreCase);

            foreach (var required in UnsafeWindowMvReservedColumns.CommonMetadata)
            {
                if (!columnSet.Contains(required))
                {
                    throw new InvalidOperationException($"Unsafe window MV table '{table}' is missing required metadata column '{required}'.");
                }
            }

            var tableSpecific = table == resolver.SafeTable
                ? UnsafeWindowMvReservedColumns.SafeOnlyMetadata
                : UnsafeWindowMvReservedColumns.UnsafeOnlyMetadata;
            foreach (var required in tableSpecific)
            {
                if (!columnSet.Contains(required))
                {
                    throw new InvalidOperationException($"Unsafe window MV table '{table}' is missing required metadata column '{required}'.");
                }
            }

            foreach (var business in resolver.Schema.Columns)
            {
                if (!columnSet.Contains(business.Name))
                {
                    throw new InvalidOperationException(
                        $"Unsafe window MV table '{table}' is missing declared business column '{business.Name}'. Drop the table or bump the view version to reshape the schema.");
                }
            }
        }
    }
}

public sealed class UnsafeWindowMvMySqlInitializer : IUnsafeWindowMvInitializer
{
    private readonly UnsafeWindowMvMySqlSchemaResolver _resolver;
    private readonly string _connectionString;
    private readonly ILogger _logger;

    public UnsafeWindowMvMySqlInitializer(UnsafeWindowMvMySqlSchemaResolver resolver, string connectionString, ILogger logger)
    {
        _resolver = resolver;
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var statement in UnsafeWindowMvMySqlDdl.BuildInitializeStatements(_resolver))
        {
            await connection.ExecuteAsync(new CommandDefinition(statement, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        await UnsafeWindowMvMySqlValidator.ValidateAsync(_resolver, connection, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Unsafe window MV '{View}' v{Version} initialized on MySQL (safe={Safe}, unsafe={Unsafe}, view={Current}, view_live={CurrentLive}).",
            _resolver.ViewName,
            _resolver.ViewVersion,
            _resolver.SafeTable,
            _resolver.UnsafeTable,
            _resolver.CurrentView,
            _resolver.CurrentLiveView);
    }
}

public sealed class UnsafeWindowMvMySqlStreamApplier<TRow> where TRow : class, new()
{
    private readonly UnsafeWindowMvMySqlSchemaResolver _resolver;
    private readonly IUnsafeWindowMvProjector<TRow> _projector;
    private readonly TimeSpan _safeWindow;

    public UnsafeWindowMvMySqlStreamApplier(
        UnsafeWindowMvMySqlSchemaResolver resolver,
        IUnsafeWindowMvProjector<TRow> projector)
    {
        _resolver = resolver;
        _projector = projector;
        _safeWindow = projector.SafeWindow;
    }

    public async Task ApplyEventAsync(
        MySqlConnection connection,
        IDbTransaction transaction,
        Event ev,
        CancellationToken ct)
    {
        var projectionKey = _projector.GetProjectionKey(ev);
        if (projectionKey is null)
        {
            return;
        }

        var existingRow = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            $"SELECT * FROM {_resolver.UnsafeTable} WHERE {UnsafeWindowMvReservedColumns.ProjectionKey} = @Key FOR UPDATE",
            new { Key = projectionKey },
            transaction: transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        if (existingRow is IDictionary<string, object> existingDict)
        {
            var existingSuid = (string)existingDict[UnsafeWindowMvReservedColumns.LastSortableUniqueId];
            if (string.CompareOrdinal(ev.SortableUniqueIdValue, existingSuid) <= 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    $"UPDATE {_resolver.UnsafeTable} SET {UnsafeWindowMvReservedColumns.NeedsRebuild} = 1 WHERE {UnsafeWindowMvReservedColumns.ProjectionKey} = @Key",
                    new { Key = projectionKey },
                    transaction: transaction,
                    cancellationToken: ct)).ConfigureAwait(false);
                return;
            }
        }

        TRow? current = null;
        IDictionary<string, object>? fallbackBusinessValues = null;
        long existingEventVersion = 0;
        if (existingRow is IDictionary<string, object> existing)
        {
            current = UnsafeWindowMvRowHydrator<TRow>.Hydrate(_projector.Schema, existing);
            existingEventVersion = Convert.ToInt64(existing[UnsafeWindowMvReservedColumns.LastEventVersion]);
        }
        else
        {
            var safeRow = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(
                $"SELECT * FROM {_resolver.SafeTable} WHERE {UnsafeWindowMvReservedColumns.ProjectionKey} = @Key FOR UPDATE",
                new { Key = projectionKey },
                transaction: transaction,
                cancellationToken: ct)).ConfigureAwait(false);
            if (safeRow is IDictionary<string, object> safeDict)
            {
                var safeSuid = (string)safeDict[UnsafeWindowMvReservedColumns.LastSortableUniqueId];
                existingEventVersion = Convert.ToInt64(safeDict[UnsafeWindowMvReservedColumns.LastEventVersion]);
                if (string.CompareOrdinal(ev.SortableUniqueIdValue, safeSuid) <= 0)
                {
                    await MirrorSafeIntoUnsafeForRebuildAsync(connection, transaction, projectionKey, safeDict, ct)
                        .ConfigureAwait(false);
                    return;
                }

                current = UnsafeWindowMvRowHydrator<TRow>.Hydrate(_projector.Schema, safeDict);
                fallbackBusinessValues = safeDict;
            }
        }

        var outcome = _projector.Apply(current, ev);
        var newEventVersion = existingEventVersion + 1;
        switch (outcome)
        {
            case UnsafeWindowMvApplyOutcome.NoChange:
                return;
            case UnsafeWindowMvApplyOutcome.Upsert up:
                if (!string.Equals(up.ProjectionKey, projectionKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Projector '{_projector.ViewName}' returned a different projection key from Apply ('{up.ProjectionKey}') than GetProjectionKey ('{projectionKey}').");
                }
                await UpsertUnsafeAsync(connection, transaction, projectionKey, up.Row, isDeleted: false, ev, newEventVersion, ct).ConfigureAwait(false);
                break;
            case UnsafeWindowMvApplyOutcome.Delete del:
                if (!string.Equals(del.ProjectionKey, projectionKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Projector '{_projector.ViewName}' returned a different projection key from Apply ('{del.ProjectionKey}') than GetProjectionKey ('{projectionKey}').");
                }
                await UpsertDeletionAsync(connection, transaction, projectionKey, current, fallbackBusinessValues, ev, newEventVersion, ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task MirrorSafeIntoUnsafeForRebuildAsync(
        MySqlConnection connection,
        IDbTransaction transaction,
        string projectionKey,
        IDictionary<string, object> safeDict,
        CancellationToken ct)
    {
        var businessCols = _projector.Schema.Columns.Select(c => c.Name).ToList();
        var businessValueParams = string.Join(", ", businessCols.Select(c => "@" + c));

        var sql = $"""
            INSERT INTO {_resolver.UnsafeTable}
                ({UnsafeWindowMvReservedColumns.ProjectionKey},
                 {string.Join(", ", businessCols)},
                 {UnsafeWindowMvReservedColumns.IsDeleted},
                 {UnsafeWindowMvReservedColumns.LastSortableUniqueId},
                 {UnsafeWindowMvReservedColumns.LastEventVersion},
                 {UnsafeWindowMvReservedColumns.LastAppliedAt},
                 {UnsafeWindowMvReservedColumns.UnsafeSince},
                 {UnsafeWindowMvReservedColumns.SafeDueAt},
                 {UnsafeWindowMvReservedColumns.NeedsRebuild})
            VALUES (@__projectionKey, {businessValueParams}, @__isDeleted, @__suid, @__eventVersion, CURRENT_TIMESTAMP(6), CURRENT_TIMESTAMP(6), CURRENT_TIMESTAMP(6), 1)
            ON DUPLICATE KEY UPDATE
                {UnsafeWindowMvReservedColumns.NeedsRebuild} = 1;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("__projectionKey", projectionKey);
        parameters.Add("__isDeleted", Convert.ToBoolean(safeDict[UnsafeWindowMvReservedColumns.IsDeleted]));
        parameters.Add("__suid", safeDict[UnsafeWindowMvReservedColumns.LastSortableUniqueId]);
        parameters.Add("__eventVersion", Convert.ToInt64(safeDict[UnsafeWindowMvReservedColumns.LastEventVersion]));
        foreach (var column in _projector.Schema.Columns)
        {
            parameters.Add(column.Name, safeDict.TryGetValue(column.Name, out var v) ? (v ?? DBNull.Value) : DBNull.Value);
        }

        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    private async Task UpsertUnsafeAsync(
        MySqlConnection connection,
        IDbTransaction transaction,
        string projectionKey,
        object row,
        bool isDeleted,
        Event ev,
        long eventVersion,
        CancellationToken ct)
    {
        var businessCols = _projector.Schema.Columns.Select(c => c.Name).ToList();
        var safeDueAt = DateTime.UtcNow.Add(_safeWindow);
        var businessValueParams = string.Join(", ", businessCols.Select(c => "@" + c));
        var businessUpdates = string.Join(", ", businessCols.Select(c => $"{c} = VALUES({c})"));

        var sql = $"""
            INSERT INTO {_resolver.UnsafeTable}
                ({UnsafeWindowMvReservedColumns.ProjectionKey},
                 {string.Join(", ", businessCols)},
                 {UnsafeWindowMvReservedColumns.IsDeleted},
                 {UnsafeWindowMvReservedColumns.LastSortableUniqueId},
                 {UnsafeWindowMvReservedColumns.LastEventVersion},
                 {UnsafeWindowMvReservedColumns.LastAppliedAt},
                 {UnsafeWindowMvReservedColumns.UnsafeSince},
                 {UnsafeWindowMvReservedColumns.SafeDueAt},
                 {UnsafeWindowMvReservedColumns.NeedsRebuild})
            VALUES (@__projectionKey, {businessValueParams}, @__isDeleted, @__suid, @__eventVersion, CURRENT_TIMESTAMP(6), CURRENT_TIMESTAMP(6), @__safeDueAt, 0)
            ON DUPLICATE KEY UPDATE
                {businessUpdates},
                {UnsafeWindowMvReservedColumns.IsDeleted} = VALUES({UnsafeWindowMvReservedColumns.IsDeleted}),
                {UnsafeWindowMvReservedColumns.LastSortableUniqueId} = VALUES({UnsafeWindowMvReservedColumns.LastSortableUniqueId}),
                {UnsafeWindowMvReservedColumns.LastEventVersion} = VALUES({UnsafeWindowMvReservedColumns.LastEventVersion}),
                {UnsafeWindowMvReservedColumns.LastAppliedAt} = CURRENT_TIMESTAMP(6),
                {UnsafeWindowMvReservedColumns.SafeDueAt} = VALUES({UnsafeWindowMvReservedColumns.SafeDueAt}),
                {UnsafeWindowMvReservedColumns.NeedsRebuild} = 0;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("__projectionKey", projectionKey);
        parameters.Add("__isDeleted", isDeleted);
        parameters.Add("__suid", ev.SortableUniqueIdValue);
        parameters.Add("__eventVersion", eventVersion);
        parameters.Add("__safeDueAt", safeDueAt);

        foreach (var column in _projector.Schema.Columns)
        {
            parameters.Add(column.Name, column.Getter(row) ?? DBNull.Value);
        }

        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    private async Task UpsertDeletionAsync(
        MySqlConnection connection,
        IDbTransaction transaction,
        string projectionKey,
        TRow? currentRow,
        IDictionary<string, object>? fallbackBusinessValues,
        Event ev,
        long eventVersion,
        CancellationToken ct)
    {
        if (currentRow is null && fallbackBusinessValues is null)
        {
            return;
        }

        var safeDueAt = DateTime.UtcNow.Add(_safeWindow);
        var businessCols = _projector.Schema.Columns.Select(c => c.Name).ToList();
        var businessValueParams = string.Join(", ", businessCols.Select(c => "@" + c));
        var businessUpdates = string.Join(", ", businessCols.Select(c => $"{c} = VALUES({c})"));

        var sql = $"""
            INSERT INTO {_resolver.UnsafeTable}
                ({UnsafeWindowMvReservedColumns.ProjectionKey},
                 {string.Join(", ", businessCols)},
                 {UnsafeWindowMvReservedColumns.IsDeleted},
                 {UnsafeWindowMvReservedColumns.LastSortableUniqueId},
                 {UnsafeWindowMvReservedColumns.LastEventVersion},
                 {UnsafeWindowMvReservedColumns.LastAppliedAt},
                 {UnsafeWindowMvReservedColumns.UnsafeSince},
                 {UnsafeWindowMvReservedColumns.SafeDueAt},
                 {UnsafeWindowMvReservedColumns.NeedsRebuild})
            VALUES (@__projectionKey, {businessValueParams}, 1, @__suid, @__eventVersion, CURRENT_TIMESTAMP(6), CURRENT_TIMESTAMP(6), @__safeDueAt, 0)
            ON DUPLICATE KEY UPDATE
                {businessUpdates},
                {UnsafeWindowMvReservedColumns.IsDeleted} = 1,
                {UnsafeWindowMvReservedColumns.LastSortableUniqueId} = VALUES({UnsafeWindowMvReservedColumns.LastSortableUniqueId}),
                {UnsafeWindowMvReservedColumns.LastEventVersion} = VALUES({UnsafeWindowMvReservedColumns.LastEventVersion}),
                {UnsafeWindowMvReservedColumns.LastAppliedAt} = CURRENT_TIMESTAMP(6),
                {UnsafeWindowMvReservedColumns.SafeDueAt} = VALUES({UnsafeWindowMvReservedColumns.SafeDueAt}),
                {UnsafeWindowMvReservedColumns.NeedsRebuild} = 0;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("__projectionKey", projectionKey);
        parameters.Add("__suid", ev.SortableUniqueIdValue);
        parameters.Add("__eventVersion", eventVersion);
        parameters.Add("__safeDueAt", safeDueAt);

        foreach (var column in _projector.Schema.Columns)
        {
            object? value;
            if (currentRow is not null)
            {
                value = column.Getter(currentRow);
            }
            else if (fallbackBusinessValues is not null && fallbackBusinessValues.TryGetValue(column.Name, out var v))
            {
                value = v;
            }
            else
            {
                value = null;
            }
            parameters.Add(column.Name, value ?? DBNull.Value);
        }

        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: ct))
            .ConfigureAwait(false);
    }
}

public sealed class UnsafeWindowMvMySqlCatchUpWorker<TRow> : IUnsafeWindowMvCatchUpRunner where TRow : class, new()
{
    private readonly UnsafeWindowMvMySqlSchemaResolver _resolver;
    private readonly IUnsafeWindowMvProjector<TRow> _projector;
    private readonly UnsafeWindowMvMySqlStreamApplier<TRow> _applier;
    private readonly IEventStore _eventStore;
    private readonly IEventTypes _eventTypes;
    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly int _batchSize;

    public UnsafeWindowMvMySqlCatchUpWorker(
        UnsafeWindowMvMySqlSchemaResolver resolver,
        IUnsafeWindowMvProjector<TRow> projector,
        IEventStore eventStore,
        IEventTypes eventTypes,
        string connectionString,
        ILogger logger,
        int batchSize = 256)
    {
        _resolver = resolver;
        _projector = projector;
        _applier = new UnsafeWindowMvMySqlStreamApplier<TRow>(resolver, projector);
        _eventStore = eventStore;
        _eventTypes = eventTypes;
        _connectionString = connectionString;
        _logger = logger;
        _batchSize = batchSize;
    }

    public async Task<int> CatchUpOnceAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var lastSuid = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            $"""
             SELECT COALESCE(MAX(x.suid), '')
             FROM (
                 SELECT {UnsafeWindowMvReservedColumns.LastSortableUniqueId} AS suid FROM {_resolver.SafeTable}
                 UNION ALL
                 SELECT {UnsafeWindowMvReservedColumns.LastSortableUniqueId} AS suid FROM {_resolver.UnsafeTable}
             ) x
             """,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var since = string.IsNullOrEmpty(lastSuid) ? null : new SortableUniqueId(lastSuid);
        var result = await _eventStore.ReadAllSerializableEventsAsync(since).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw result.GetException();
        }

        var events = result.GetValue().Take(_batchSize).ToList();
        if (events.Count == 0)
        {
            return 0;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var serialized in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evResult = serialized.ToEvent(_eventTypes);
            if (!evResult.IsSuccess)
            {
                throw evResult.GetException();
            }

            await _applier.ApplyEventAsync(connection, transaction, evResult.GetValue(), cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Unsafe window MV '{View}' caught up {Count} event(s) on MySQL.", _projector.ViewName, events.Count);
        return events.Count;
    }
}

public sealed class UnsafeWindowMvMySqlPromoter<TRow> : IUnsafeWindowMvPromoter where TRow : class, new()
{
    private readonly UnsafeWindowMvMySqlSchemaResolver _resolver;
    private readonly IUnsafeWindowMvProjector<TRow> _projector;
    private readonly IEventStore _eventStore;
    private readonly IEventTypes _eventTypes;
    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly int _batchSize;

    public UnsafeWindowMvMySqlPromoter(
        UnsafeWindowMvMySqlSchemaResolver resolver,
        IUnsafeWindowMvProjector<TRow> projector,
        IEventStore eventStore,
        IEventTypes eventTypes,
        string connectionString,
        ILogger logger,
        int batchSize = 32)
    {
        _resolver = resolver;
        _projector = projector;
        _eventStore = eventStore;
        _eventTypes = eventTypes;
        _connectionString = connectionString;
        _logger = logger;
        _batchSize = batchSize;
    }

    public async Task<int> PromoteOnceAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // MySQL 8.0+ supports FOR UPDATE SKIP LOCKED.
        var dueRows = (await connection.QueryAsync<(string key, bool needs_rebuild)>(new CommandDefinition(
            $"""
             SELECT {UnsafeWindowMvReservedColumns.ProjectionKey} AS `key`,
                    {UnsafeWindowMvReservedColumns.NeedsRebuild} AS needs_rebuild
             FROM {_resolver.UnsafeTable}
             WHERE {UnsafeWindowMvReservedColumns.SafeDueAt} <= CURRENT_TIMESTAMP(6)
             ORDER BY {UnsafeWindowMvReservedColumns.SafeDueAt}
             LIMIT @Limit
             FOR UPDATE SKIP LOCKED
             """,
            new { Limit = _batchSize },
            transaction: transaction,
            cancellationToken: cancellationToken))).ToList();

        if (dueRows.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        foreach (var (projectionKey, needsRebuild) in dueRows)
        {
            await PromoteKeyAsync(connection, transaction, projectionKey, needsRebuild, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Unsafe window MV '{View}' promoted {Count} key(s) on MySQL.", _projector.ViewName, dueRows.Count);
        return dueRows.Count;
    }

    private async Task PromoteKeyAsync(
        MySqlConnection connection,
        IDbTransaction transaction,
        string projectionKey,
        bool needsRebuild,
        CancellationToken ct)
    {
        var safeRow = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            $"SELECT * FROM {_resolver.SafeTable} WHERE {UnsafeWindowMvReservedColumns.ProjectionKey} = @Key",
            new { Key = projectionKey },
            transaction: transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        TRow? current = null;
        string? startSuid = null;
        long startVersion = 0;
        var isDeleted = false;

        if (!needsRebuild && safeRow is IDictionary<string, object> safeDict)
        {
            current = UnsafeWindowMvRowHydrator<TRow>.Hydrate(_projector.Schema, safeDict);
            startSuid = (string)safeDict[UnsafeWindowMvReservedColumns.LastSortableUniqueId];
            startVersion = Convert.ToInt64(safeDict[UnsafeWindowMvReservedColumns.LastEventVersion]);
            isDeleted = Convert.ToBoolean(safeDict[UnsafeWindowMvReservedColumns.IsDeleted]);
        }

        var events = await FetchReplayEventsAsync(projectionKey, startSuid, ct).ConfigureAwait(false);

        var lastSuid = startSuid;
        var lastEventVersion = startVersion;

        foreach (var ev in events)
        {
            var outcome = _projector.Apply(current, ev);
            switch (outcome)
            {
                case UnsafeWindowMvApplyOutcome.NoChange:
                    break;
                case UnsafeWindowMvApplyOutcome.Upsert up:
                    current = (TRow)up.Row;
                    isDeleted = false;
                    break;
                case UnsafeWindowMvApplyOutcome.Delete:
                    isDeleted = true;
                    break;
            }

            lastSuid = ev.SortableUniqueIdValue;
            lastEventVersion++;
        }

        if (lastSuid is null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {_resolver.UnsafeTable} WHERE {UnsafeWindowMvReservedColumns.ProjectionKey} = @Key",
                new { Key = projectionKey },
                transaction: transaction,
                cancellationToken: ct)).ConfigureAwait(false);
            return;
        }

        await UpsertSafeAsync(connection, transaction, projectionKey, current, isDeleted, lastSuid, lastEventVersion, ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {_resolver.UnsafeTable} WHERE {UnsafeWindowMvReservedColumns.ProjectionKey} = @Key",
            new { Key = projectionKey },
            transaction: transaction,
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Event>> FetchReplayEventsAsync(string projectionKey, string? sinceSuid, CancellationToken ct)
    {
        var since = string.IsNullOrEmpty(sinceSuid) ? null : new SortableUniqueId(sinceSuid);
        var combined = new Dictionary<string, Event>(StringComparer.Ordinal);

        var tags = _projector.TagsForProjectionKey(projectionKey);
        if (tags.Count == 0)
        {
            return [];
        }

        foreach (var tag in tags)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _eventStore.ReadSerializableEventsByTagAsync(tag, since).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                throw result.GetException();
            }

            foreach (var serialized in result.GetValue())
            {
                var key = serialized.Id.ToString();
                if (combined.ContainsKey(key))
                {
                    continue;
                }

                var evResult = serialized.ToEvent(_eventTypes);
                if (!evResult.IsSuccess)
                {
                    throw evResult.GetException();
                }
                combined[key] = evResult.GetValue();
            }
        }

        return combined.Values
            .Where(e => sinceSuid is null || string.CompareOrdinal(e.SortableUniqueIdValue, sinceSuid) > 0)
            .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task UpsertSafeAsync(
        MySqlConnection connection,
        IDbTransaction transaction,
        string projectionKey,
        TRow? row,
        bool isDeleted,
        string sortableUniqueId,
        long eventVersion,
        CancellationToken ct)
    {
        var businessCols = _projector.Schema.Columns.Select(c => c.Name).ToList();
        var businessValueParams = string.Join(", ", businessCols.Select(c => "@" + c));
        var businessUpdates = string.Join(", ", businessCols.Select(c => $"{c} = VALUES({c})"));

        var sql = $"""
            INSERT INTO {_resolver.SafeTable}
                ({UnsafeWindowMvReservedColumns.ProjectionKey},
                 {string.Join(", ", businessCols)},
                 {UnsafeWindowMvReservedColumns.IsDeleted},
                 {UnsafeWindowMvReservedColumns.LastSortableUniqueId},
                 {UnsafeWindowMvReservedColumns.LastEventVersion},
                 {UnsafeWindowMvReservedColumns.LastAppliedAt},
                 {UnsafeWindowMvReservedColumns.SafeConfirmedAt})
            VALUES (@__projectionKey, {businessValueParams}, @__isDeleted, @__suid, @__eventVersion, CURRENT_TIMESTAMP(6), CURRENT_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                {businessUpdates},
                {UnsafeWindowMvReservedColumns.IsDeleted} = VALUES({UnsafeWindowMvReservedColumns.IsDeleted}),
                {UnsafeWindowMvReservedColumns.LastSortableUniqueId} = VALUES({UnsafeWindowMvReservedColumns.LastSortableUniqueId}),
                {UnsafeWindowMvReservedColumns.LastEventVersion} = VALUES({UnsafeWindowMvReservedColumns.LastEventVersion}),
                {UnsafeWindowMvReservedColumns.LastAppliedAt} = CURRENT_TIMESTAMP(6),
                {UnsafeWindowMvReservedColumns.SafeConfirmedAt} = CURRENT_TIMESTAMP(6);
            """;

        var parameters = new DynamicParameters();
        parameters.Add("__projectionKey", projectionKey);
        parameters.Add("__isDeleted", isDeleted);
        parameters.Add("__suid", sortableUniqueId);
        parameters.Add("__eventVersion", eventVersion);

        foreach (var column in _projector.Schema.Columns)
        {
            parameters.Add(column.Name, row is null ? DBNull.Value : (column.Getter(row) ?? DBNull.Value));
        }

        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: ct))
            .ConfigureAwait(false);
    }
}
