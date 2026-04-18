using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.MaterializedView.SqlServer;

// ============================================================================
// Unsafe Window Materialized View — SQL Server runtime (issue #1035).
//
// Mirrors the Postgres runtime shape (UnsafeWindowMvPostgresRuntime.cs) but
// uses SQL Server SQL dialect:
//   - NVARCHAR(MAX) for TEXT, NVARCHAR(450) for PRIMARY KEY columns so
//     the index stays within SQL Server's 900-byte key limit.
//   - BIT for BOOLEAN.
//   - DATETIMEOFFSET for TIMESTAMPTZ, SYSUTCDATETIME() for NOW().
//   - UNIQUEIDENTIFIER for UUID.
//   - UPDATE … WITH (UPDLOCK, HOLDLOCK) + conditional INSERT in place of
//     ON CONFLICT (same upsert pattern as the classic SQL Server MV
//     registry; avoids MERGE's concurrency pitfalls).
//   - WITH (UPDLOCK, HOLDLOCK, ROWLOCK) for FOR UPDATE, WITH (UPDLOCK,
//     READPAST, ROWLOCK) for FOR UPDATE SKIP LOCKED.
// ============================================================================

public sealed class UnsafeWindowMvSqlServerSchemaResolver
{
    // SQL Server object names allow up to 128 Unicode chars, but we stay within
    // 63 bytes to keep identifiers portable with Postgres and MySQL. The 13-byte
    // "_current_live" suffix still needs to fit within the bound.
    private const int MaxSharedPrefixLength = 63 - 13;

    public UnsafeWindowMvSqlServerSchemaResolver(string viewName, int viewVersion, UnsafeWindowMvSchema schema)
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

internal static class UnsafeWindowMvSqlServerTypes
{
    // Portable Postgres-style keywords a projector can use in SqlType are
    // translated to their SQL Server equivalents when emitting DDL so the
    // same projector definition works on every supported provider.
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["TEXT"] = "NVARCHAR(MAX)",
        ["BOOLEAN"] = "BIT",
        ["BOOL"] = "BIT",
        ["UUID"] = "UNIQUEIDENTIFIER",
        ["TIMESTAMPTZ"] = "DATETIMEOFFSET",
        ["TIMESTAMP"] = "DATETIME2",
        ["BIGINT"] = "BIGINT",
        ["INT"] = "INT",
        ["INTEGER"] = "INT",
        ["DATE"] = "DATE",
        ["REAL"] = "REAL",
        ["DOUBLE"] = "FLOAT",
        ["DOUBLE PRECISION"] = "FLOAT",
        ["SMALLINT"] = "SMALLINT"
    };

    public static string Translate(string declaredType) =>
        UnsafeWindowMvLogicalTypes.Translate(declaredType, Map);
}

internal static class UnsafeWindowMvSqlServerDdl
{
    public static IReadOnlyList<string> BuildInitializeStatements(UnsafeWindowMvSqlServerSchemaResolver resolver)
    {
        var businessDdl = string.Join(
            ",\n    ",
            resolver.Schema.Columns.Select(c => $"{c.Name} {UnsafeWindowMvSqlServerTypes.Translate(c.SqlType)}"));

        var safeTable = $"""
            IF OBJECT_ID(N'{resolver.SafeTable}', N'U') IS NULL
            BEGIN
                CREATE TABLE {resolver.SafeTable} (
                    {UnsafeWindowMvReservedColumns.ProjectionKey} NVARCHAR(450) NOT NULL PRIMARY KEY,
                    {businessDdl},
                    {UnsafeWindowMvReservedColumns.IsDeleted} BIT NOT NULL CONSTRAINT DF_{resolver.SafeTable}_is_deleted DEFAULT 0,
                    {UnsafeWindowMvReservedColumns.LastSortableUniqueId} NVARCHAR(64) NOT NULL,
                    {UnsafeWindowMvReservedColumns.LastEventVersion} BIGINT NOT NULL CONSTRAINT DF_{resolver.SafeTable}_last_event_version DEFAULT 0,
                    {UnsafeWindowMvReservedColumns.LastAppliedAt} DATETIMEOFFSET NOT NULL CONSTRAINT DF_{resolver.SafeTable}_last_applied_at DEFAULT SYSUTCDATETIME(),
                    {UnsafeWindowMvReservedColumns.SafeConfirmedAt} DATETIMEOFFSET NOT NULL CONSTRAINT DF_{resolver.SafeTable}_safe_confirmed_at DEFAULT SYSUTCDATETIME()
                );
            END;
            """;

        var unsafeTable = $"""
            IF OBJECT_ID(N'{resolver.UnsafeTable}', N'U') IS NULL
            BEGIN
                CREATE TABLE {resolver.UnsafeTable} (
                    {UnsafeWindowMvReservedColumns.ProjectionKey} NVARCHAR(450) NOT NULL PRIMARY KEY,
                    {businessDdl},
                    {UnsafeWindowMvReservedColumns.IsDeleted} BIT NOT NULL CONSTRAINT DF_{resolver.UnsafeTable}_is_deleted DEFAULT 0,
                    {UnsafeWindowMvReservedColumns.LastSortableUniqueId} NVARCHAR(64) NOT NULL,
                    {UnsafeWindowMvReservedColumns.LastEventVersion} BIGINT NOT NULL CONSTRAINT DF_{resolver.UnsafeTable}_last_event_version DEFAULT 0,
                    {UnsafeWindowMvReservedColumns.LastAppliedAt} DATETIMEOFFSET NOT NULL CONSTRAINT DF_{resolver.UnsafeTable}_last_applied_at DEFAULT SYSUTCDATETIME(),
                    {UnsafeWindowMvReservedColumns.UnsafeSince} DATETIMEOFFSET NOT NULL CONSTRAINT DF_{resolver.UnsafeTable}_unsafe_since DEFAULT SYSUTCDATETIME(),
                    {UnsafeWindowMvReservedColumns.SafeDueAt} DATETIMEOFFSET NOT NULL CONSTRAINT DF_{resolver.UnsafeTable}_safe_due_at DEFAULT SYSUTCDATETIME(),
                    {UnsafeWindowMvReservedColumns.NeedsRebuild} BIT NOT NULL CONSTRAINT DF_{resolver.UnsafeTable}_needs_rebuild DEFAULT 0
                );
            END;
            """;

        var indexName = $"idx_{resolver.UnsafeTable}_safe_due_at";
        var unsafeDueIdx = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID(N'{resolver.UnsafeTable}'))
            BEGIN
                CREATE INDEX {indexName} ON {resolver.UnsafeTable} ({UnsafeWindowMvReservedColumns.SafeDueAt});
            END;
            """;

        var businessSelect = string.Join(", ", resolver.Schema.Columns.Select(c => c.Name));

        var dropCurrentView = $"IF OBJECT_ID(N'{resolver.CurrentView}', N'V') IS NOT NULL DROP VIEW {resolver.CurrentView};";
        var createCurrentView = $"""
            EXEC(N'CREATE VIEW {resolver.CurrentView} AS
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
            );');
            """;

        var dropCurrentLiveView = $"IF OBJECT_ID(N'{resolver.CurrentLiveView}', N'V') IS NOT NULL DROP VIEW {resolver.CurrentLiveView};";
        var createCurrentLiveView = $"""
            EXEC(N'CREATE VIEW {resolver.CurrentLiveView} AS
            SELECT * FROM {resolver.CurrentView} WHERE {UnsafeWindowMvReservedColumns.IsDeleted} = 0;');
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

internal static class UnsafeWindowMvSqlServerValidator
{
    public static async Task ValidateAsync(
        UnsafeWindowMvSqlServerSchemaResolver resolver,
        SqlConnection connection,
        CancellationToken ct)
    {
        foreach (var table in new[] { resolver.SafeTable, resolver.UnsafeTable })
        {
            var columns = (await connection.QueryAsync<(string column_name, string data_type)>(
                new CommandDefinition(
                    """
                    SELECT column_name, data_type
                    FROM information_schema.columns
                    WHERE table_schema = SCHEMA_NAME()
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

public sealed class UnsafeWindowMvSqlServerInitializer : IUnsafeWindowMvInitializer
{
    private readonly UnsafeWindowMvSqlServerSchemaResolver _resolver;
    private readonly string _connectionString;
    private readonly ILogger _logger;

    public UnsafeWindowMvSqlServerInitializer(UnsafeWindowMvSqlServerSchemaResolver resolver, string connectionString, ILogger logger)
    {
        _resolver = resolver;
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var statement in UnsafeWindowMvSqlServerDdl.BuildInitializeStatements(_resolver))
        {
            await connection.ExecuteAsync(new CommandDefinition(statement, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        await UnsafeWindowMvSqlServerValidator.ValidateAsync(_resolver, connection, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Unsafe window MV '{View}' v{Version} initialized on SQL Server (safe={Safe}, unsafe={Unsafe}, view={Current}, view_live={CurrentLive}).",
            _resolver.ViewName,
            _resolver.ViewVersion,
            _resolver.SafeTable,
            _resolver.UnsafeTable,
            _resolver.CurrentView,
            _resolver.CurrentLiveView);
    }
}

public sealed class UnsafeWindowMvSqlServerStreamApplier<TRow> where TRow : class, new()
{
    private readonly UnsafeWindowMvSqlServerSchemaResolver _resolver;
    private readonly IUnsafeWindowMvProjector<TRow> _projector;
    private readonly TimeSpan _safeWindow;

    public UnsafeWindowMvSqlServerStreamApplier(
        UnsafeWindowMvSqlServerSchemaResolver resolver,
        IUnsafeWindowMvProjector<TRow> projector)
    {
        _resolver = resolver;
        _projector = projector;
        _safeWindow = projector.SafeWindow;
    }

    public async Task ApplyEventAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        Event ev,
        CancellationToken ct)
    {
        var projectionKey = _projector.GetProjectionKey(ev);
        if (projectionKey is null)
        {
            return;
        }

        // FOR UPDATE equivalent on SQL Server: WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
        // HOLDLOCK upgrades to SERIALIZABLE for the key range, which keeps the
        // upsert correct under concurrent stream writes.
        var existingRow = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            $"SELECT * FROM {_resolver.UnsafeTable} WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE {UnsafeWindowMvReservedColumns.ProjectionKey} = @Key",
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
                $"SELECT * FROM {_resolver.SafeTable} WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE {UnsafeWindowMvReservedColumns.ProjectionKey} = @Key",
                new { Key = projectionKey },
                transaction: transaction,
                cancellationToken: ct)).ConfigureAwait(false);
            if (safeRow is IDictionary<string, object> safeDict)
            {
                var safeSuid = (string)safeDict[UnsafeWindowMvReservedColumns.LastSortableUniqueId];
                existingEventVersion = Convert.ToInt64(safeDict[UnsafeWindowMvReservedColumns.LastEventVersion]);
                if (string.CompareOrdinal(ev.SortableUniqueIdValue, safeSuid) <= 0)
                {
                    await MirrorSafeIntoUnsafeForRebuildAsync(
                            connection,
                            transaction,
                            projectionKey,
                            safeDict,
                            ct)
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
                        $"Projector '{_projector.ViewName}' returned a different projection key from Apply ('{up.ProjectionKey}') than GetProjectionKey ('{projectionKey}'). The two must agree.");
                }
                await UpsertUnsafeAsync(connection, transaction, projectionKey, up.Row, isDeleted: false, ev, newEventVersion, ct).ConfigureAwait(false);
                break;
            case UnsafeWindowMvApplyOutcome.Delete del:
                if (!string.Equals(del.ProjectionKey, projectionKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Projector '{_projector.ViewName}' returned a different projection key from Apply ('{del.ProjectionKey}') than GetProjectionKey ('{projectionKey}'). The two must agree.");
                }
                await UpsertDeletionAsync(connection, transaction, projectionKey, current, fallbackBusinessValues, ev, newEventVersion, ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task MirrorSafeIntoUnsafeForRebuildAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        string projectionKey,
        IDictionary<string, object> safeDict,
        CancellationToken ct)
    {
        var businessCols = _projector.Schema.Columns.Select(c => c.Name).ToList();
        var parameters = new DynamicParameters();
        parameters.Add("__projectionKey", projectionKey);
        parameters.Add("__isDeleted", safeDict[UnsafeWindowMvReservedColumns.IsDeleted]);
        parameters.Add("__suid", safeDict[UnsafeWindowMvReservedColumns.LastSortableUniqueId]);
        parameters.Add("__eventVersion", Convert.ToInt64(safeDict[UnsafeWindowMvReservedColumns.LastEventVersion]));
        foreach (var column in _projector.Schema.Columns)
        {
            parameters.Add(column.Name, safeDict.TryGetValue(column.Name, out var v) ? (v ?? DBNull.Value) : DBNull.Value);
        }

        // SQL Server upsert: UPDATE (HOLDLOCK) then INSERT when no row was
        // updated. HOLDLOCK keeps the key range locked for the branch, so a
        // concurrent caller cannot race an INSERT into the same key.
        var businessValueParams = string.Join(", ", businessCols.Select(c => "@" + c));
        var sql = $"""
            UPDATE {_resolver.UnsafeTable} WITH (UPDLOCK, HOLDLOCK)
            SET {UnsafeWindowMvReservedColumns.NeedsRebuild} = 1
            WHERE {UnsafeWindowMvReservedColumns.ProjectionKey} = @__projectionKey;

            IF @@ROWCOUNT = 0
            BEGIN
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
                VALUES (@__projectionKey, {businessValueParams}, @__isDeleted, @__suid, @__eventVersion, SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME(), 1);
            END;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    private async Task UpsertUnsafeAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        string projectionKey,
        object row,
        bool isDeleted,
        Event ev,
        long eventVersion,
        CancellationToken ct)
    {
        var businessCols = _projector.Schema.Columns.Select(c => c.Name).ToList();
        var safeDueAt = DateTimeOffset.UtcNow.Add(_safeWindow);

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

        var businessUpdates = string.Join(", ", businessCols.Select(c => $"{c} = @{c}"));
        var businessInsertCols = string.Join(", ", businessCols);
        var businessInsertValues = string.Join(", ", businessCols.Select(c => "@" + c));

        var sql = $"""
            UPDATE {_resolver.UnsafeTable} WITH (UPDLOCK, HOLDLOCK)
            SET {businessUpdates},
                {UnsafeWindowMvReservedColumns.IsDeleted} = @__isDeleted,
                {UnsafeWindowMvReservedColumns.LastSortableUniqueId} = @__suid,
                {UnsafeWindowMvReservedColumns.LastEventVersion} = @__eventVersion,
                {UnsafeWindowMvReservedColumns.LastAppliedAt} = SYSUTCDATETIME(),
                {UnsafeWindowMvReservedColumns.SafeDueAt} = @__safeDueAt,
                {UnsafeWindowMvReservedColumns.NeedsRebuild} = 0
            WHERE {UnsafeWindowMvReservedColumns.ProjectionKey} = @__projectionKey;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO {_resolver.UnsafeTable}
                    ({UnsafeWindowMvReservedColumns.ProjectionKey},
                     {businessInsertCols},
                     {UnsafeWindowMvReservedColumns.IsDeleted},
                     {UnsafeWindowMvReservedColumns.LastSortableUniqueId},
                     {UnsafeWindowMvReservedColumns.LastEventVersion},
                     {UnsafeWindowMvReservedColumns.LastAppliedAt},
                     {UnsafeWindowMvReservedColumns.UnsafeSince},
                     {UnsafeWindowMvReservedColumns.SafeDueAt},
                     {UnsafeWindowMvReservedColumns.NeedsRebuild})
                VALUES (@__projectionKey, {businessInsertValues}, @__isDeleted, @__suid, @__eventVersion, SYSUTCDATETIME(), SYSUTCDATETIME(), @__safeDueAt, 0);
            END;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    private async Task UpsertDeletionAsync(
        SqlConnection connection,
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

        var safeDueAt = DateTimeOffset.UtcNow.Add(_safeWindow);
        var businessCols = _projector.Schema.Columns.Select(c => c.Name).ToList();

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

        var businessUpdates = string.Join(", ", businessCols.Select(c => $"{c} = @{c}"));
        var businessInsertCols = string.Join(", ", businessCols);
        var businessInsertValues = string.Join(", ", businessCols.Select(c => "@" + c));

        var sql = $"""
            UPDATE {_resolver.UnsafeTable} WITH (UPDLOCK, HOLDLOCK)
            SET {businessUpdates},
                {UnsafeWindowMvReservedColumns.IsDeleted} = 1,
                {UnsafeWindowMvReservedColumns.LastSortableUniqueId} = @__suid,
                {UnsafeWindowMvReservedColumns.LastEventVersion} = @__eventVersion,
                {UnsafeWindowMvReservedColumns.LastAppliedAt} = SYSUTCDATETIME(),
                {UnsafeWindowMvReservedColumns.SafeDueAt} = @__safeDueAt,
                {UnsafeWindowMvReservedColumns.NeedsRebuild} = 0
            WHERE {UnsafeWindowMvReservedColumns.ProjectionKey} = @__projectionKey;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO {_resolver.UnsafeTable}
                    ({UnsafeWindowMvReservedColumns.ProjectionKey},
                     {businessInsertCols},
                     {UnsafeWindowMvReservedColumns.IsDeleted},
                     {UnsafeWindowMvReservedColumns.LastSortableUniqueId},
                     {UnsafeWindowMvReservedColumns.LastEventVersion},
                     {UnsafeWindowMvReservedColumns.LastAppliedAt},
                     {UnsafeWindowMvReservedColumns.UnsafeSince},
                     {UnsafeWindowMvReservedColumns.SafeDueAt},
                     {UnsafeWindowMvReservedColumns.NeedsRebuild})
                VALUES (@__projectionKey, {businessInsertValues}, 1, @__suid, @__eventVersion, SYSUTCDATETIME(), SYSUTCDATETIME(), @__safeDueAt, 0);
            END;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: ct))
            .ConfigureAwait(false);
    }
}

public sealed class UnsafeWindowMvSqlServerCatchUpWorker<TRow> : IUnsafeWindowMvCatchUpRunner where TRow : class, new()
{
    private readonly UnsafeWindowMvSqlServerSchemaResolver _resolver;
    private readonly IUnsafeWindowMvProjector<TRow> _projector;
    private readonly UnsafeWindowMvSqlServerStreamApplier<TRow> _applier;
    private readonly IEventStore _eventStore;
    private readonly IEventTypes _eventTypes;
    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly int _batchSize;

    public UnsafeWindowMvSqlServerCatchUpWorker(
        UnsafeWindowMvSqlServerSchemaResolver resolver,
        IUnsafeWindowMvProjector<TRow> projector,
        IEventStore eventStore,
        IEventTypes eventTypes,
        string connectionString,
        ILogger logger,
        int batchSize = 256)
    {
        _resolver = resolver;
        _projector = projector;
        _applier = new UnsafeWindowMvSqlServerStreamApplier<TRow>(resolver, projector);
        _eventStore = eventStore;
        _eventTypes = eventTypes;
        _connectionString = connectionString;
        _logger = logger;
        _batchSize = batchSize;
    }

    public async Task<int> CatchUpOnceAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
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
        _logger.LogDebug("Unsafe window MV '{View}' caught up {Count} event(s) on SQL Server.", _projector.ViewName, events.Count);
        return events.Count;
    }
}

public sealed class UnsafeWindowMvSqlServerPromoter<TRow> : IUnsafeWindowMvPromoter where TRow : class, new()
{
    private readonly UnsafeWindowMvSqlServerSchemaResolver _resolver;
    private readonly IUnsafeWindowMvProjector<TRow> _projector;
    private readonly IEventStore _eventStore;
    private readonly IEventTypes _eventTypes;
    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly int _batchSize;

    public UnsafeWindowMvSqlServerPromoter(
        UnsafeWindowMvSqlServerSchemaResolver resolver,
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
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // FOR UPDATE SKIP LOCKED equivalent: READPAST + UPDLOCK + ROWLOCK lets
        // concurrent promoters across the silo each grab a different set of due
        // keys without blocking on each other.
        var dueRows = (await connection.QueryAsync<(string key, bool needs_rebuild)>(new CommandDefinition(
            $"""
             SELECT TOP (@Limit)
                    {UnsafeWindowMvReservedColumns.ProjectionKey} AS [key],
                    {UnsafeWindowMvReservedColumns.NeedsRebuild} AS needs_rebuild
             FROM {_resolver.UnsafeTable} WITH (UPDLOCK, READPAST, ROWLOCK)
             WHERE {UnsafeWindowMvReservedColumns.SafeDueAt} <= SYSUTCDATETIME()
             ORDER BY {UnsafeWindowMvReservedColumns.SafeDueAt}
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
        _logger.LogDebug("Unsafe window MV '{View}' promoted {Count} key(s) on SQL Server.", _projector.ViewName, dueRows.Count);
        return dueRows.Count;
    }

    private async Task PromoteKeyAsync(
        SqlConnection connection,
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
        SqlConnection connection,
        IDbTransaction transaction,
        string projectionKey,
        TRow? row,
        bool isDeleted,
        string sortableUniqueId,
        long eventVersion,
        CancellationToken ct)
    {
        var businessCols = _projector.Schema.Columns.Select(c => c.Name).ToList();

        var parameters = new DynamicParameters();
        parameters.Add("__projectionKey", projectionKey);
        parameters.Add("__isDeleted", isDeleted);
        parameters.Add("__suid", sortableUniqueId);
        parameters.Add("__eventVersion", eventVersion);

        foreach (var column in _projector.Schema.Columns)
        {
            parameters.Add(column.Name, row is null ? DBNull.Value : (column.Getter(row) ?? DBNull.Value));
        }

        var businessUpdates = string.Join(", ", businessCols.Select(c => $"{c} = @{c}"));
        var businessInsertCols = string.Join(", ", businessCols);
        var businessInsertValues = string.Join(", ", businessCols.Select(c => "@" + c));

        var sql = $"""
            UPDATE {_resolver.SafeTable} WITH (UPDLOCK, HOLDLOCK)
            SET {businessUpdates},
                {UnsafeWindowMvReservedColumns.IsDeleted} = @__isDeleted,
                {UnsafeWindowMvReservedColumns.LastSortableUniqueId} = @__suid,
                {UnsafeWindowMvReservedColumns.LastEventVersion} = @__eventVersion,
                {UnsafeWindowMvReservedColumns.LastAppliedAt} = SYSUTCDATETIME(),
                {UnsafeWindowMvReservedColumns.SafeConfirmedAt} = SYSUTCDATETIME()
            WHERE {UnsafeWindowMvReservedColumns.ProjectionKey} = @__projectionKey;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO {_resolver.SafeTable}
                    ({UnsafeWindowMvReservedColumns.ProjectionKey},
                     {businessInsertCols},
                     {UnsafeWindowMvReservedColumns.IsDeleted},
                     {UnsafeWindowMvReservedColumns.LastSortableUniqueId},
                     {UnsafeWindowMvReservedColumns.LastEventVersion},
                     {UnsafeWindowMvReservedColumns.LastAppliedAt},
                     {UnsafeWindowMvReservedColumns.SafeConfirmedAt})
                VALUES (@__projectionKey, {businessInsertValues}, @__isDeleted, @__suid, @__eventVersion, SYSUTCDATETIME(), SYSUTCDATETIME());
            END;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: ct))
            .ConfigureAwait(false);
    }
}
