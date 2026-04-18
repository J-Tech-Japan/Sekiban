using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.MaterializedView.Sqlite;

// ============================================================================
// Unsafe Window Materialized View — SQLite runtime (issue #1035).
//
// Dialect notes:
//   - TEXT for keys / business strings, INTEGER for booleans, DATETIME values
//     stored as ISO 8601 TEXT.
//   - CURRENT_TIMESTAMP returns UTC ISO 8601 text by default.
//   - ON CONFLICT (col) DO UPDATE SET is supported since SQLite 3.24 (2018).
//   - No FOR UPDATE / SKIP LOCKED: SQLite serialises writers globally, so
//     the promoter naturally never races another writer on the same file.
//   - Schema validation via pragma_table_info(table).
// ============================================================================

public sealed class UnsafeWindowMvSqliteSchemaResolver
{
    private const int MaxSharedPrefixLength = 63 - 13;

    public UnsafeWindowMvSqliteSchemaResolver(string viewName, int viewVersion, UnsafeWindowMvSchema schema)
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

internal static class UnsafeWindowMvSqliteTypes
{
    // SQLite's type system is dynamic / affinity-based so most declarations
    // are informational. We still translate common logical keywords so the
    // declared types stay consistent with the other providers.
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["TEXT"] = "TEXT",
        ["BOOLEAN"] = "INTEGER",
        ["BOOL"] = "INTEGER",
        ["UUID"] = "TEXT",
        ["TIMESTAMPTZ"] = "TEXT",
        ["TIMESTAMP"] = "TEXT",
        ["BIGINT"] = "INTEGER",
        ["INT"] = "INTEGER",
        ["INTEGER"] = "INTEGER",
        ["DATE"] = "TEXT",
        ["REAL"] = "REAL",
        ["DOUBLE"] = "REAL",
        ["DOUBLE PRECISION"] = "REAL",
        ["SMALLINT"] = "INTEGER"
    };

    public static string Translate(string declaredType) =>
        UnsafeWindowMvLogicalTypes.Translate(declaredType, Map);
}

internal static class UnsafeWindowMvSqliteDdl
{
    public static IReadOnlyList<string> BuildInitializeStatements(UnsafeWindowMvSqliteSchemaResolver resolver)
    {
        var businessDdl = string.Join(
            ",\n    ",
            resolver.Schema.Columns.Select(c => $"{c.Name} {UnsafeWindowMvSqliteTypes.Translate(c.SqlType)}"));

        var safeTable = $"""
            CREATE TABLE IF NOT EXISTS {resolver.SafeTable} (
                {UnsafeWindowMvReservedColumns.ProjectionKey} TEXT PRIMARY KEY,
                {businessDdl},
                {UnsafeWindowMvReservedColumns.IsDeleted} INTEGER NOT NULL DEFAULT 0,
                {UnsafeWindowMvReservedColumns.LastSortableUniqueId} TEXT NOT NULL,
                {UnsafeWindowMvReservedColumns.LastEventVersion} INTEGER NOT NULL DEFAULT 0,
                {UnsafeWindowMvReservedColumns.LastAppliedAt} TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
                {UnsafeWindowMvReservedColumns.SafeConfirmedAt} TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP)
            );
            """;

        var unsafeTable = $"""
            CREATE TABLE IF NOT EXISTS {resolver.UnsafeTable} (
                {UnsafeWindowMvReservedColumns.ProjectionKey} TEXT PRIMARY KEY,
                {businessDdl},
                {UnsafeWindowMvReservedColumns.IsDeleted} INTEGER NOT NULL DEFAULT 0,
                {UnsafeWindowMvReservedColumns.LastSortableUniqueId} TEXT NOT NULL,
                {UnsafeWindowMvReservedColumns.LastEventVersion} INTEGER NOT NULL DEFAULT 0,
                {UnsafeWindowMvReservedColumns.LastAppliedAt} TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
                {UnsafeWindowMvReservedColumns.UnsafeSince} TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
                {UnsafeWindowMvReservedColumns.SafeDueAt} TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
                {UnsafeWindowMvReservedColumns.NeedsRebuild} INTEGER NOT NULL DEFAULT 0
            );
            """;

        var unsafeDueIdx = $"CREATE INDEX IF NOT EXISTS idx_{resolver.UnsafeTable}_safe_due_at ON {resolver.UnsafeTable} ({UnsafeWindowMvReservedColumns.SafeDueAt});";

        // SQLite doesn't support CREATE OR REPLACE VIEW. Recreate with drop+create.
        var dropCurrentView = $"DROP VIEW IF EXISTS {resolver.CurrentView};";
        var createCurrentView = $"""
            CREATE VIEW {resolver.CurrentView} AS
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
            );
            """;

        var dropCurrentLiveView = $"DROP VIEW IF EXISTS {resolver.CurrentLiveView};";
        var createCurrentLiveView = $"""
            CREATE VIEW {resolver.CurrentLiveView} AS
            SELECT *
            FROM {resolver.CurrentView}
            WHERE {UnsafeWindowMvReservedColumns.IsDeleted} = 0;
            """;

        return
        [
            safeTable,
            unsafeTable,
            unsafeDueIdx,
            dropCurrentView,
            createCurrentView,
            dropCurrentLiveView,
            createCurrentLiveView
        ];
    }
}

internal static class UnsafeWindowMvSqliteValidator
{
    public static async Task ValidateAsync(
        UnsafeWindowMvSqliteSchemaResolver resolver,
        SqliteConnection connection,
        CancellationToken ct)
    {
        foreach (var table in new[] { resolver.SafeTable, resolver.UnsafeTable })
        {
            // pragma_table_info is SQLite's introspection source.
            var columns = (await connection.QueryAsync<(string name, string type)>(
                new CommandDefinition(
                    $"SELECT name, type FROM pragma_table_info('{table}')",
                    cancellationToken: ct))).ToList();

            if (columns.Count == 0)
            {
                throw new InvalidOperationException($"Unsafe window MV table '{table}' was not created by initialization.");
            }

            var columnSet = new HashSet<string>(columns.Select(c => c.name), StringComparer.OrdinalIgnoreCase);

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

public sealed class UnsafeWindowMvSqliteInitializer : IUnsafeWindowMvInitializer
{
    private readonly UnsafeWindowMvSqliteSchemaResolver _resolver;
    private readonly string _connectionString;
    private readonly ILogger _logger;

    public UnsafeWindowMvSqliteInitializer(UnsafeWindowMvSqliteSchemaResolver resolver, string connectionString, ILogger logger)
    {
        _resolver = resolver;
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Enable WAL + NORMAL synchronous up front so that subsequent writes
        // (catch-up + promotion) don't block readers. journal_mode survives
        // the connection; synchronous is per-connection.
        await connection.ExecuteAsync(new CommandDefinition(
            "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;",
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        foreach (var statement in UnsafeWindowMvSqliteDdl.BuildInitializeStatements(_resolver))
        {
            await connection.ExecuteAsync(new CommandDefinition(statement, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        await UnsafeWindowMvSqliteValidator.ValidateAsync(_resolver, connection, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Unsafe window MV '{View}' v{Version} initialized on SQLite (safe={Safe}, unsafe={Unsafe}, view={Current}, view_live={CurrentLive}).",
            _resolver.ViewName,
            _resolver.ViewVersion,
            _resolver.SafeTable,
            _resolver.UnsafeTable,
            _resolver.CurrentView,
            _resolver.CurrentLiveView);
    }
}

public sealed class UnsafeWindowMvSqliteStreamApplier<TRow> where TRow : class, new()
{
    private readonly UnsafeWindowMvSqliteSchemaResolver _resolver;
    private readonly IUnsafeWindowMvProjector<TRow> _projector;
    private readonly TimeSpan _safeWindow;

    public UnsafeWindowMvSqliteStreamApplier(
        UnsafeWindowMvSqliteSchemaResolver resolver,
        IUnsafeWindowMvProjector<TRow> projector)
    {
        _resolver = resolver;
        _projector = projector;
        _safeWindow = projector.SafeWindow;
    }

    public async Task ApplyEventAsync(
        SqliteConnection connection,
        IDbTransaction transaction,
        Event ev,
        CancellationToken ct)
    {
        var projectionKey = _projector.GetProjectionKey(ev);
        if (projectionKey is null)
        {
            return;
        }

        // SQLite serializes writers; no row-level SELECT FOR UPDATE is needed
        // or available. The surrounding transaction is enough.
        var existingRow = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            $"SELECT * FROM {_resolver.UnsafeTable} WHERE {UnsafeWindowMvReservedColumns.ProjectionKey} = @Key",
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
                $"SELECT * FROM {_resolver.SafeTable} WHERE {UnsafeWindowMvReservedColumns.ProjectionKey} = @Key",
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
        SqliteConnection connection,
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
            VALUES (@__projectionKey, {businessValueParams}, @__isDeleted, @__suid, @__eventVersion, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 1)
            ON CONFLICT ({UnsafeWindowMvReservedColumns.ProjectionKey}) DO UPDATE SET
                {UnsafeWindowMvReservedColumns.NeedsRebuild} = 1;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("__projectionKey", projectionKey);
        parameters.Add("__isDeleted", Convert.ToInt64(safeDict[UnsafeWindowMvReservedColumns.IsDeleted]));
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
        SqliteConnection connection,
        IDbTransaction transaction,
        string projectionKey,
        object row,
        bool isDeleted,
        Event ev,
        long eventVersion,
        CancellationToken ct)
    {
        var businessCols = _projector.Schema.Columns.Select(c => c.Name).ToList();
        // SQLite stores CURRENT_TIMESTAMP as UTC ISO text; compare safe_due_at as text ordering.
        var safeDueAt = DateTime.UtcNow.Add(_safeWindow).ToString("yyyy-MM-dd HH:mm:ss");
        var businessValueParams = string.Join(", ", businessCols.Select(c => "@" + c));
        var businessUpdates = string.Join(", ", businessCols.Select(c => $"{c} = excluded.{c}"));

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
            VALUES (@__projectionKey, {businessValueParams}, @__isDeleted, @__suid, @__eventVersion, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, @__safeDueAt, 0)
            ON CONFLICT ({UnsafeWindowMvReservedColumns.ProjectionKey}) DO UPDATE SET
                {businessUpdates},
                {UnsafeWindowMvReservedColumns.IsDeleted} = excluded.{UnsafeWindowMvReservedColumns.IsDeleted},
                {UnsafeWindowMvReservedColumns.LastSortableUniqueId} = excluded.{UnsafeWindowMvReservedColumns.LastSortableUniqueId},
                {UnsafeWindowMvReservedColumns.LastEventVersion} = excluded.{UnsafeWindowMvReservedColumns.LastEventVersion},
                {UnsafeWindowMvReservedColumns.LastAppliedAt} = CURRENT_TIMESTAMP,
                {UnsafeWindowMvReservedColumns.SafeDueAt} = excluded.{UnsafeWindowMvReservedColumns.SafeDueAt},
                {UnsafeWindowMvReservedColumns.NeedsRebuild} = 0;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("__projectionKey", projectionKey);
        parameters.Add("__isDeleted", isDeleted ? 1 : 0);
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
        SqliteConnection connection,
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

        var safeDueAt = DateTime.UtcNow.Add(_safeWindow).ToString("yyyy-MM-dd HH:mm:ss");
        var businessCols = _projector.Schema.Columns.Select(c => c.Name).ToList();
        var businessValueParams = string.Join(", ", businessCols.Select(c => "@" + c));
        var businessUpdates = string.Join(", ", businessCols.Select(c => $"{c} = excluded.{c}"));

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
            VALUES (@__projectionKey, {businessValueParams}, 1, @__suid, @__eventVersion, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, @__safeDueAt, 0)
            ON CONFLICT ({UnsafeWindowMvReservedColumns.ProjectionKey}) DO UPDATE SET
                {businessUpdates},
                {UnsafeWindowMvReservedColumns.IsDeleted} = 1,
                {UnsafeWindowMvReservedColumns.LastSortableUniqueId} = excluded.{UnsafeWindowMvReservedColumns.LastSortableUniqueId},
                {UnsafeWindowMvReservedColumns.LastEventVersion} = excluded.{UnsafeWindowMvReservedColumns.LastEventVersion},
                {UnsafeWindowMvReservedColumns.LastAppliedAt} = CURRENT_TIMESTAMP,
                {UnsafeWindowMvReservedColumns.SafeDueAt} = excluded.{UnsafeWindowMvReservedColumns.SafeDueAt},
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

public sealed class UnsafeWindowMvSqliteCatchUpWorker<TRow> : IUnsafeWindowMvCatchUpRunner where TRow : class, new()
{
    private readonly UnsafeWindowMvSqliteSchemaResolver _resolver;
    private readonly IUnsafeWindowMvProjector<TRow> _projector;
    private readonly UnsafeWindowMvSqliteStreamApplier<TRow> _applier;
    private readonly IEventStore _eventStore;
    private readonly IEventTypes _eventTypes;
    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly int _batchSize;

    public UnsafeWindowMvSqliteCatchUpWorker(
        UnsafeWindowMvSqliteSchemaResolver resolver,
        IUnsafeWindowMvProjector<TRow> projector,
        IEventStore eventStore,
        IEventTypes eventTypes,
        string connectionString,
        ILogger logger,
        int batchSize = 256)
    {
        _resolver = resolver;
        _projector = projector;
        _applier = new UnsafeWindowMvSqliteStreamApplier<TRow>(resolver, projector);
        _eventStore = eventStore;
        _eventTypes = eventTypes;
        _connectionString = connectionString;
        _logger = logger;
        _batchSize = batchSize;
    }

    public async Task<int> CatchUpOnceAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "PRAGMA synchronous=NORMAL;",
            cancellationToken: cancellationToken)).ConfigureAwait(false);

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
        _logger.LogDebug("Unsafe window MV '{View}' caught up {Count} event(s) on SQLite.", _projector.ViewName, events.Count);
        return events.Count;
    }
}

public sealed class UnsafeWindowMvSqlitePromoter<TRow> : IUnsafeWindowMvPromoter where TRow : class, new()
{
    private readonly UnsafeWindowMvSqliteSchemaResolver _resolver;
    private readonly IUnsafeWindowMvProjector<TRow> _projector;
    private readonly IEventStore _eventStore;
    private readonly IEventTypes _eventTypes;
    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly int _batchSize;

    public UnsafeWindowMvSqlitePromoter(
        UnsafeWindowMvSqliteSchemaResolver resolver,
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
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "PRAGMA synchronous=NORMAL;",
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // SQLite has no FOR UPDATE SKIP LOCKED; the file-wide writer lock
        // serialises promoters across processes naturally.
        var dueRows = (await connection.QueryAsync<(string key, long needs_rebuild)>(new CommandDefinition(
            $"""
             SELECT {UnsafeWindowMvReservedColumns.ProjectionKey} AS key,
                    {UnsafeWindowMvReservedColumns.NeedsRebuild} AS needs_rebuild
             FROM {_resolver.UnsafeTable}
             WHERE {UnsafeWindowMvReservedColumns.SafeDueAt} <= CURRENT_TIMESTAMP
             ORDER BY {UnsafeWindowMvReservedColumns.SafeDueAt}
             LIMIT @Limit
             """,
            new { Limit = _batchSize },
            transaction: transaction,
            cancellationToken: cancellationToken))).ToList();

        if (dueRows.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        foreach (var (projectionKey, needsRebuildInt) in dueRows)
        {
            await PromoteKeyAsync(connection, transaction, projectionKey, needsRebuildInt != 0, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Unsafe window MV '{View}' promoted {Count} key(s) on SQLite.", _projector.ViewName, dueRows.Count);
        return dueRows.Count;
    }

    private async Task PromoteKeyAsync(
        SqliteConnection connection,
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
            isDeleted = Convert.ToInt64(safeDict[UnsafeWindowMvReservedColumns.IsDeleted]) != 0;
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
        SqliteConnection connection,
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
        var businessUpdates = string.Join(", ", businessCols.Select(c => $"{c} = excluded.{c}"));

        var sql = $"""
            INSERT INTO {_resolver.SafeTable}
                ({UnsafeWindowMvReservedColumns.ProjectionKey},
                 {string.Join(", ", businessCols)},
                 {UnsafeWindowMvReservedColumns.IsDeleted},
                 {UnsafeWindowMvReservedColumns.LastSortableUniqueId},
                 {UnsafeWindowMvReservedColumns.LastEventVersion},
                 {UnsafeWindowMvReservedColumns.LastAppliedAt},
                 {UnsafeWindowMvReservedColumns.SafeConfirmedAt})
            VALUES (@__projectionKey, {businessValueParams}, @__isDeleted, @__suid, @__eventVersion, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT ({UnsafeWindowMvReservedColumns.ProjectionKey}) DO UPDATE SET
                {businessUpdates},
                {UnsafeWindowMvReservedColumns.IsDeleted} = excluded.{UnsafeWindowMvReservedColumns.IsDeleted},
                {UnsafeWindowMvReservedColumns.LastSortableUniqueId} = excluded.{UnsafeWindowMvReservedColumns.LastSortableUniqueId},
                {UnsafeWindowMvReservedColumns.LastEventVersion} = excluded.{UnsafeWindowMvReservedColumns.LastEventVersion},
                {UnsafeWindowMvReservedColumns.LastAppliedAt} = CURRENT_TIMESTAMP,
                {UnsafeWindowMvReservedColumns.SafeConfirmedAt} = CURRENT_TIMESTAMP;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("__projectionKey", projectionKey);
        parameters.Add("__isDeleted", isDeleted ? 1 : 0);
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
