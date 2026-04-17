using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.MaterializedView.Postgres;

// ============================================================================
// Unsafe Window Materialized View — Postgres runtime (v1, issue #1028).
//
// Layout:
//   UnsafeWindowMvColumns       — reserved metadata column names.
//   UnsafeWindowMvSchemaResolver— physical table / view naming.
//   UnsafeWindowMvDdl           — CREATE TABLE / CREATE VIEW statements.
//   UnsafeWindowMvValidator     — fail-fast schema validation on startup.
//   UnsafeWindowMvInitializer   — idempotent DDL creation + validation.
//   UnsafeWindowMvStreamApplier — stream-side apply-one-event (fast/repair).
//   UnsafeWindowMvPromoter      — background promotion worker.
//   UnsafeWindowMvRowHydrator   — rebuild TRow from DB row.
// ============================================================================

internal static class UnsafeWindowMvColumns
{
    public const string ProjectionKey = "_projection_key";
    public const string IsDeleted = "_is_deleted";
    public const string LastSortableUniqueId = "_last_sortable_unique_id";
    public const string LastEventVersion = "_last_event_version";
    public const string LastAppliedAt = "_last_applied_at";
    public const string SafeConfirmedAt = "_safe_confirmed_at";
    public const string UnsafeSince = "_unsafe_since";
    public const string SafeDueAt = "_safe_due_at";
    public const string NeedsRebuild = "_needs_rebuild";

    public static readonly string[] CommonMetadata =
    [
        ProjectionKey,
        IsDeleted,
        LastSortableUniqueId,
        LastEventVersion,
        LastAppliedAt
    ];

    public static readonly string[] SafeOnlyMetadata = [SafeConfirmedAt];

    public static readonly string[] UnsafeOnlyMetadata =
    [
        UnsafeSince,
        SafeDueAt,
        NeedsRebuild
    ];
}

public sealed class UnsafeWindowMvSchemaResolver
{
    public UnsafeWindowMvSchemaResolver(string viewName, int viewVersion, UnsafeWindowMvSchema schema)
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

        var reserved = new HashSet<string>(
            UnsafeWindowMvColumns.CommonMetadata
                .Concat(UnsafeWindowMvColumns.SafeOnlyMetadata)
                .Concat(UnsafeWindowMvColumns.UnsafeOnlyMetadata),
            StringComparer.OrdinalIgnoreCase);
        foreach (var column in schema.Columns)
        {
            if (reserved.Contains(column.Name))
            {
                throw new InvalidOperationException(
                    $"Unsafe window MV '{viewName}' declares a business column '{column.Name}' that clashes with a framework-managed metadata column. Rename the business column.");
            }
        }

        var prefix = $"sekiban_uwmv_{SanitizeSegment(viewName)}_v{viewVersion}";
        SafeTable = $"{prefix}_safe";
        UnsafeTable = $"{prefix}_unsafe";
        CurrentView = $"{prefix}_current";
        CurrentLiveView = $"{prefix}_current_live";
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

    private static string SanitizeSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                builder.Append(ch);
            }
            else
            {
                builder.Append('_');
            }
        }

        var sanitized = builder.ToString().Trim('_');
        return string.IsNullOrEmpty(sanitized) ? "view" : sanitized;
    }
}

internal static class UnsafeWindowMvDdl
{
    public static IReadOnlyList<string> BuildInitializeStatements(UnsafeWindowMvSchemaResolver resolver)
    {
        var businessDdl = string.Join(
            ",\n    ",
            resolver.Schema.Columns.Select(c => $"{c.Name} {c.SqlType}"));

        var safeTable = $"""
            CREATE TABLE IF NOT EXISTS {resolver.SafeTable} (
                {UnsafeWindowMvColumns.ProjectionKey} TEXT PRIMARY KEY,
                {businessDdl},
                {UnsafeWindowMvColumns.IsDeleted} BOOLEAN NOT NULL DEFAULT FALSE,
                {UnsafeWindowMvColumns.LastSortableUniqueId} TEXT NOT NULL,
                {UnsafeWindowMvColumns.LastEventVersion} BIGINT NOT NULL DEFAULT 0,
                {UnsafeWindowMvColumns.LastAppliedAt} TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                {UnsafeWindowMvColumns.SafeConfirmedAt} TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """;

        var unsafeTable = $"""
            CREATE TABLE IF NOT EXISTS {resolver.UnsafeTable} (
                {UnsafeWindowMvColumns.ProjectionKey} TEXT PRIMARY KEY,
                {businessDdl},
                {UnsafeWindowMvColumns.IsDeleted} BOOLEAN NOT NULL DEFAULT FALSE,
                {UnsafeWindowMvColumns.LastSortableUniqueId} TEXT NOT NULL,
                {UnsafeWindowMvColumns.LastEventVersion} BIGINT NOT NULL DEFAULT 0,
                {UnsafeWindowMvColumns.LastAppliedAt} TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                {UnsafeWindowMvColumns.UnsafeSince} TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                {UnsafeWindowMvColumns.SafeDueAt} TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                {UnsafeWindowMvColumns.NeedsRebuild} BOOLEAN NOT NULL DEFAULT FALSE
            );
            """;

        var unsafeDueIdx = $"CREATE INDEX IF NOT EXISTS idx_{resolver.UnsafeTable}_safe_due_at ON {resolver.UnsafeTable} ({UnsafeWindowMvColumns.SafeDueAt});";

        var currentView = $"""
            CREATE OR REPLACE VIEW {resolver.CurrentView} AS
            SELECT
                u.{UnsafeWindowMvColumns.ProjectionKey},
                {string.Join(", ", resolver.Schema.Columns.Select(c => $"u.{c.Name}"))},
                u.{UnsafeWindowMvColumns.IsDeleted},
                u.{UnsafeWindowMvColumns.LastSortableUniqueId},
                u.{UnsafeWindowMvColumns.LastEventVersion},
                u.{UnsafeWindowMvColumns.LastAppliedAt}
            FROM {resolver.UnsafeTable} u
            UNION ALL
            SELECT
                s.{UnsafeWindowMvColumns.ProjectionKey},
                {string.Join(", ", resolver.Schema.Columns.Select(c => $"s.{c.Name}"))},
                s.{UnsafeWindowMvColumns.IsDeleted},
                s.{UnsafeWindowMvColumns.LastSortableUniqueId},
                s.{UnsafeWindowMvColumns.LastEventVersion},
                s.{UnsafeWindowMvColumns.LastAppliedAt}
            FROM {resolver.SafeTable} s
            WHERE NOT EXISTS (
                SELECT 1 FROM {resolver.UnsafeTable} u2
                WHERE u2.{UnsafeWindowMvColumns.ProjectionKey} = s.{UnsafeWindowMvColumns.ProjectionKey}
            );
            """;

        var currentLiveView = $"""
            CREATE OR REPLACE VIEW {resolver.CurrentLiveView} AS
            SELECT *
            FROM {resolver.CurrentView}
            WHERE {UnsafeWindowMvColumns.IsDeleted} = FALSE;
            """;

        return [safeTable, unsafeTable, unsafeDueIdx, currentView, currentLiveView];
    }
}

internal static class UnsafeWindowMvValidator
{
    public static async Task ValidateAsync(
        UnsafeWindowMvSchemaResolver resolver,
        NpgsqlConnection connection,
        CancellationToken ct)
    {
        foreach (var table in new[] { resolver.SafeTable, resolver.UnsafeTable })
        {
            var columns = (await connection.QueryAsync<(string column_name, string data_type)>(
                new CommandDefinition(
                    """
                    SELECT column_name, data_type
                    FROM information_schema.columns
                    WHERE table_name = @Table
                    """,
                    new { Table = table },
                    cancellationToken: ct))).ToList();

            if (columns.Count == 0)
            {
                throw new InvalidOperationException($"Unsafe window MV table '{table}' was not created by initialization.");
            }

            var columnSet = new HashSet<string>(columns.Select(c => c.column_name), StringComparer.OrdinalIgnoreCase);

            foreach (var required in UnsafeWindowMvColumns.CommonMetadata)
            {
                if (!columnSet.Contains(required))
                {
                    throw new InvalidOperationException($"Unsafe window MV table '{table}' is missing required metadata column '{required}'.");
                }
            }

            var tableSpecific = table == resolver.SafeTable
                ? UnsafeWindowMvColumns.SafeOnlyMetadata
                : UnsafeWindowMvColumns.UnsafeOnlyMetadata;
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

public sealed class UnsafeWindowMvInitializer
{
    private readonly UnsafeWindowMvSchemaResolver _resolver;
    private readonly string _connectionString;
    private readonly ILogger _logger;

    public UnsafeWindowMvInitializer(UnsafeWindowMvSchemaResolver resolver, string connectionString, ILogger logger)
    {
        _resolver = resolver;
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var statement in UnsafeWindowMvDdl.BuildInitializeStatements(_resolver))
        {
            await connection.ExecuteAsync(new CommandDefinition(statement, transaction: transaction, cancellationToken: ct))
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        await UnsafeWindowMvValidator.ValidateAsync(_resolver, connection, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Unsafe window MV '{View}' v{Version} initialized (safe={Safe}, unsafe={Unsafe}, view={Current}, view_live={CurrentLive}).",
            _resolver.ViewName,
            _resolver.ViewVersion,
            _resolver.SafeTable,
            _resolver.UnsafeTable,
            _resolver.CurrentView,
            _resolver.CurrentLiveView);
    }
}

internal static class UnsafeWindowMvRowHydrator<TRow> where TRow : class
{
    public static TRow Hydrate(UnsafeWindowMvSchema schema, IDictionary<string, object> dbRow)
    {
        var ctor = typeof(TRow).GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 0)
            ?? throw new InvalidOperationException(
                $"Row type '{typeof(TRow).FullName}' must expose a parameterless constructor so the runtime can rebuild rows read from the unsafe/safe tables.");

        var instance = (TRow)ctor.Invoke(null);
        foreach (var column in schema.Columns)
        {
            if (!dbRow.TryGetValue(column.Name, out var value) || value is null || value is DBNull)
            {
                continue;
            }

            var property = typeof(TRow).GetProperty(PascalCaseFromSnake(column.Name), BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanWrite)
            {
                continue;
            }

            property.SetValue(instance, CoerceValue(value, property.PropertyType));
        }

        return instance;
    }

    private static object? CoerceValue(object value, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying.IsInstanceOfType(value))
        {
            return value;
        }

        if (underlying == typeof(DateOnly))
        {
            return value switch
            {
                DateTime dt => DateOnly.FromDateTime(dt),
                DateTimeOffset dto => DateOnly.FromDateTime(dto.UtcDateTime),
                _ => value
            };
        }
        if (underlying == typeof(Guid) && value is string str && Guid.TryParse(str, out var g))
        {
            return g;
        }

        try
        {
            return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
        }
        catch
        {
            return value;
        }
    }

    private static string PascalCaseFromSnake(string name)
    {
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                sb.Append(part[1..]);
            }
        }
        return sb.ToString();
    }
}

public sealed class UnsafeWindowMvStreamApplier<TRow> where TRow : class
{
    private readonly UnsafeWindowMvSchemaResolver _resolver;
    private readonly IUnsafeWindowMvProjector<TRow> _projector;
    private readonly TimeSpan _safeWindow;

    public UnsafeWindowMvStreamApplier(
        UnsafeWindowMvSchemaResolver resolver,
        IUnsafeWindowMvProjector<TRow> projector)
    {
        _resolver = resolver;
        _projector = projector;
        _safeWindow = projector.SafeWindow;
    }

    public async Task ApplyEventAsync(
        NpgsqlConnection connection,
        IDbTransaction transaction,
        Event ev,
        CancellationToken ct)
    {
        // Determine the target projection key from the event payload alone. If
        // the projector is not interested in this event we stop early and avoid
        // the SELECT.
        var projectionKey = _projector.GetProjectionKey(ev);
        if (projectionKey is null)
        {
            return;
        }

        var existingRow = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            $"SELECT * FROM {_resolver.UnsafeTable} WHERE {UnsafeWindowMvColumns.ProjectionKey} = @Key FOR UPDATE",
            new { Key = projectionKey },
            transaction: transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        if (existingRow is IDictionary<string, object> existingDict)
        {
            var existingSuid = (string)existingDict[UnsafeWindowMvColumns.LastSortableUniqueId];
            if (string.CompareOrdinal(ev.SortableUniqueIdValue, existingSuid) <= 0)
            {
                // Repair path: incoming event is not strictly newer. Mark for
                // rebuild so safe-promotion replay fixes it; do NOT overwrite
                // the newer row with the older event.
                await connection.ExecuteAsync(new CommandDefinition(
                    $"UPDATE {_resolver.UnsafeTable} SET {UnsafeWindowMvColumns.NeedsRebuild} = TRUE WHERE {UnsafeWindowMvColumns.ProjectionKey} = @Key",
                    new { Key = projectionKey },
                    transaction: transaction,
                    cancellationToken: ct)).ConfigureAwait(false);
                return;
            }
        }

        // If the unsafe row is empty, the previous state for this key may
        // already have been promoted to safe. Hydrate from safe so partial
        // updates (e.g. a field-rename event that depends on the current row)
        // can fold correctly.
        TRow? current = null;
        IDictionary<string, object>? fallbackBusinessValues = null;
        if (existingRow is IDictionary<string, object> existing)
        {
            current = UnsafeWindowMvRowHydrator<TRow>.Hydrate(_projector.Schema, existing);
        }
        else
        {
            var safeRow = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(
                $"SELECT * FROM {_resolver.SafeTable} WHERE {UnsafeWindowMvColumns.ProjectionKey} = @Key",
                new { Key = projectionKey },
                transaction: transaction,
                cancellationToken: ct)).ConfigureAwait(false);
            if (safeRow is IDictionary<string, object> safeDict)
            {
                // Events that land before their create (or well after a previous
                // promotion has completed) can reference a key that lives only in
                // safe. Fold against the safe row so the unsafe upsert reflects
                // the full state.
                current = UnsafeWindowMvRowHydrator<TRow>.Hydrate(_projector.Schema, safeDict);
                fallbackBusinessValues = safeDict;
            }
        }

        var outcome = _projector.Apply(current, ev);
        switch (outcome)
        {
            case UnsafeWindowMvApplyOutcome.NoChange:
                return;
            case UnsafeWindowMvApplyOutcome.Upsert up:
                await UpsertUnsafeAsync(connection, transaction, up.ProjectionKey, up.Row, isDeleted: false, ev, ct).ConfigureAwait(false);
                break;
            case UnsafeWindowMvApplyOutcome.Delete del:
                await UpsertDeletionAsync(connection, transaction, del.ProjectionKey, current, fallbackBusinessValues, ev, ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task UpsertUnsafeAsync(
        NpgsqlConnection connection,
        IDbTransaction transaction,
        string projectionKey,
        object row,
        bool isDeleted,
        Event ev,
        CancellationToken ct)
    {
        var businessCols = _projector.Schema.Columns.Select(c => c.Name).ToList();
        var safeDueAt = DateTimeOffset.UtcNow.Add(_safeWindow);
        var businessValueParams = string.Join(", ", businessCols.Select(c => "@" + c));
        var businessUpdates = string.Join(", ", businessCols.Select(c => $"{c} = EXCLUDED.{c}"));

        var sql = $"""
            INSERT INTO {_resolver.UnsafeTable}
                ({UnsafeWindowMvColumns.ProjectionKey},
                 {string.Join(", ", businessCols)},
                 {UnsafeWindowMvColumns.IsDeleted},
                 {UnsafeWindowMvColumns.LastSortableUniqueId},
                 {UnsafeWindowMvColumns.LastEventVersion},
                 {UnsafeWindowMvColumns.LastAppliedAt},
                 {UnsafeWindowMvColumns.UnsafeSince},
                 {UnsafeWindowMvColumns.SafeDueAt},
                 {UnsafeWindowMvColumns.NeedsRebuild})
            VALUES (@__projectionKey, {businessValueParams}, @__isDeleted, @__suid, @__eventVersion, NOW(), NOW(), @__safeDueAt, FALSE)
            ON CONFLICT ({UnsafeWindowMvColumns.ProjectionKey}) DO UPDATE SET
                {businessUpdates},
                {UnsafeWindowMvColumns.IsDeleted} = EXCLUDED.{UnsafeWindowMvColumns.IsDeleted},
                {UnsafeWindowMvColumns.LastSortableUniqueId} = EXCLUDED.{UnsafeWindowMvColumns.LastSortableUniqueId},
                {UnsafeWindowMvColumns.LastEventVersion} = EXCLUDED.{UnsafeWindowMvColumns.LastEventVersion},
                {UnsafeWindowMvColumns.LastAppliedAt} = NOW(),
                {UnsafeWindowMvColumns.SafeDueAt} = EXCLUDED.{UnsafeWindowMvColumns.SafeDueAt},
                {UnsafeWindowMvColumns.NeedsRebuild} = FALSE;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("__projectionKey", projectionKey);
        parameters.Add("__isDeleted", isDeleted);
        parameters.Add("__suid", ev.SortableUniqueIdValue);
        parameters.Add("__eventVersion", 0L);
        parameters.Add("__safeDueAt", safeDueAt);

        foreach (var column in _projector.Schema.Columns)
        {
            parameters.Add(column.Name, column.Getter(row) ?? DBNull.Value);
        }

        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    private async Task UpsertDeletionAsync(
        NpgsqlConnection connection,
        IDbTransaction transaction,
        string projectionKey,
        TRow? currentRow,
        IDictionary<string, object>? fallbackBusinessValues,
        Event ev,
        CancellationToken ct)
    {
        // Tombstones retain the last-known business values so the user's
        // NOT NULL constraints keep holding and a `current` read that
        // consults `_is_deleted` still shows meaningful context. Use (in
        // priority) the currentRow we folded against, the safe row we
        // loaded as fallback, or skip entirely if no prior row exists.
        if (currentRow is null && fallbackBusinessValues is null)
        {
            // Nothing to tombstone yet. A delete of a key we have never seen
            // is effectively a no-op.
            return;
        }

        var safeDueAt = DateTimeOffset.UtcNow.Add(_safeWindow);
        var businessCols = _projector.Schema.Columns.Select(c => c.Name).ToList();
        var businessValueParams = string.Join(", ", businessCols.Select(c => "@" + c));
        var businessUpdates = string.Join(", ", businessCols.Select(c => $"{c} = EXCLUDED.{c}"));

        var sql = $"""
            INSERT INTO {_resolver.UnsafeTable}
                ({UnsafeWindowMvColumns.ProjectionKey},
                 {string.Join(", ", businessCols)},
                 {UnsafeWindowMvColumns.IsDeleted},
                 {UnsafeWindowMvColumns.LastSortableUniqueId},
                 {UnsafeWindowMvColumns.LastEventVersion},
                 {UnsafeWindowMvColumns.LastAppliedAt},
                 {UnsafeWindowMvColumns.UnsafeSince},
                 {UnsafeWindowMvColumns.SafeDueAt},
                 {UnsafeWindowMvColumns.NeedsRebuild})
            VALUES (@__projectionKey, {businessValueParams}, TRUE, @__suid, @__eventVersion, NOW(), NOW(), @__safeDueAt, FALSE)
            ON CONFLICT ({UnsafeWindowMvColumns.ProjectionKey}) DO UPDATE SET
                {businessUpdates},
                {UnsafeWindowMvColumns.IsDeleted} = TRUE,
                {UnsafeWindowMvColumns.LastSortableUniqueId} = EXCLUDED.{UnsafeWindowMvColumns.LastSortableUniqueId},
                {UnsafeWindowMvColumns.LastEventVersion} = EXCLUDED.{UnsafeWindowMvColumns.LastEventVersion},
                {UnsafeWindowMvColumns.LastAppliedAt} = NOW(),
                {UnsafeWindowMvColumns.SafeDueAt} = EXCLUDED.{UnsafeWindowMvColumns.SafeDueAt},
                {UnsafeWindowMvColumns.NeedsRebuild} = FALSE;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("__projectionKey", projectionKey);
        parameters.Add("__suid", ev.SortableUniqueIdValue);
        parameters.Add("__eventVersion", 0L);
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

public sealed class UnsafeWindowMvPromoter<TRow> where TRow : class
{
    private readonly UnsafeWindowMvSchemaResolver _resolver;
    private readonly IUnsafeWindowMvProjector<TRow> _projector;
    private readonly IEventStore _eventStore;
    private readonly IEventTypes _eventTypes;
    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly int _batchSize;

    public UnsafeWindowMvPromoter(
        UnsafeWindowMvSchemaResolver resolver,
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

    public async Task<int> PromoteOnceAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var due = (await connection.QueryAsync<string>(new CommandDefinition(
            $"""
             SELECT {UnsafeWindowMvColumns.ProjectionKey}
             FROM {_resolver.UnsafeTable}
             WHERE {UnsafeWindowMvColumns.SafeDueAt} <= NOW()
             ORDER BY {UnsafeWindowMvColumns.SafeDueAt}
             LIMIT @Limit
             FOR UPDATE SKIP LOCKED
             """,
            new { Limit = _batchSize },
            transaction: transaction,
            cancellationToken: ct))).ToList();

        if (due.Count == 0)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return 0;
        }

        foreach (var projectionKey in due)
        {
            await PromoteKeyAsync(connection, transaction, projectionKey, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        _logger.LogDebug("Unsafe window MV '{View}' promoted {Count} key(s).", _projector.ViewName, due.Count);
        return due.Count;
    }

    private async Task PromoteKeyAsync(
        NpgsqlConnection connection,
        IDbTransaction transaction,
        string projectionKey,
        CancellationToken ct)
    {
        var safeRow = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            $"SELECT * FROM {_resolver.SafeTable} WHERE {UnsafeWindowMvColumns.ProjectionKey} = @Key",
            new { Key = projectionKey },
            transaction: transaction,
            cancellationToken: ct)).ConfigureAwait(false);

        TRow? current = null;
        string? startSuid = null;
        long startVersion = 0;
        var isDeleted = false;

        if (safeRow is IDictionary<string, object> safeDict)
        {
            current = UnsafeWindowMvRowHydrator<TRow>.Hydrate(_projector.Schema, safeDict);
            startSuid = (string)safeDict[UnsafeWindowMvColumns.LastSortableUniqueId];
            startVersion = Convert.ToInt64(safeDict[UnsafeWindowMvColumns.LastEventVersion]);
            isDeleted = (bool)safeDict[UnsafeWindowMvColumns.IsDeleted];
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
                    // Keep the last-known column values so safe retains them
                    // alongside `_is_deleted = true`; matches design §11.
                    break;
            }

            lastSuid = ev.SortableUniqueIdValue;
            lastEventVersion++;
        }

        if (lastSuid is null)
        {
            // No replay events and no previous safe row — the unsafe row must
            // have been stale. Drop it so promotion stops retrying.
            await connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {_resolver.UnsafeTable} WHERE {UnsafeWindowMvColumns.ProjectionKey} = @Key",
                new { Key = projectionKey },
                transaction: transaction,
                cancellationToken: ct)).ConfigureAwait(false);
            return;
        }

        await UpsertSafeAsync(connection, transaction, projectionKey, current, isDeleted, lastSuid, lastEventVersion, ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {_resolver.UnsafeTable} WHERE {UnsafeWindowMvColumns.ProjectionKey} = @Key",
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

        // Stable ordering by sortable unique id. Events that pre-date the safe
        // row's SUID are filtered out — they have already been folded into it.
        return combined.Values
            .Where(e => sinceSuid is null || string.CompareOrdinal(e.SortableUniqueIdValue, sinceSuid) > 0)
            .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task UpsertSafeAsync(
        NpgsqlConnection connection,
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
        var businessUpdates = string.Join(", ", businessCols.Select(c => $"{c} = EXCLUDED.{c}"));

        var sql = $"""
            INSERT INTO {_resolver.SafeTable}
                ({UnsafeWindowMvColumns.ProjectionKey},
                 {string.Join(", ", businessCols)},
                 {UnsafeWindowMvColumns.IsDeleted},
                 {UnsafeWindowMvColumns.LastSortableUniqueId},
                 {UnsafeWindowMvColumns.LastEventVersion},
                 {UnsafeWindowMvColumns.LastAppliedAt},
                 {UnsafeWindowMvColumns.SafeConfirmedAt})
            VALUES (@__projectionKey, {businessValueParams}, @__isDeleted, @__suid, @__eventVersion, NOW(), NOW())
            ON CONFLICT ({UnsafeWindowMvColumns.ProjectionKey}) DO UPDATE SET
                {businessUpdates},
                {UnsafeWindowMvColumns.IsDeleted} = EXCLUDED.{UnsafeWindowMvColumns.IsDeleted},
                {UnsafeWindowMvColumns.LastSortableUniqueId} = EXCLUDED.{UnsafeWindowMvColumns.LastSortableUniqueId},
                {UnsafeWindowMvColumns.LastEventVersion} = EXCLUDED.{UnsafeWindowMvColumns.LastEventVersion},
                {UnsafeWindowMvColumns.LastAppliedAt} = NOW(),
                {UnsafeWindowMvColumns.SafeConfirmedAt} = NOW();
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

/// <summary>
///     Orchestrates catch-up from the event store into <c>unsafe</c>. Reads
///     the latest SUID applied to unsafe and streams events since that point
///     through <see cref="UnsafeWindowMvStreamApplier{TRow}" />.
/// </summary>
public sealed class UnsafeWindowMvCatchUpWorker<TRow> where TRow : class
{
    private readonly UnsafeWindowMvSchemaResolver _resolver;
    private readonly IUnsafeWindowMvProjector<TRow> _projector;
    private readonly UnsafeWindowMvStreamApplier<TRow> _applier;
    private readonly IEventStore _eventStore;
    private readonly IEventTypes _eventTypes;
    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly int _batchSize;

    public UnsafeWindowMvCatchUpWorker(
        UnsafeWindowMvSchemaResolver resolver,
        IUnsafeWindowMvProjector<TRow> projector,
        IEventStore eventStore,
        IEventTypes eventTypes,
        string connectionString,
        ILogger logger,
        int batchSize = 256)
    {
        _resolver = resolver;
        _projector = projector;
        _applier = new UnsafeWindowMvStreamApplier<TRow>(resolver, projector);
        _eventStore = eventStore;
        _eventTypes = eventTypes;
        _connectionString = connectionString;
        _logger = logger;
        _batchSize = batchSize;
    }

    public async Task<int> CatchUpOnceAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        // Pick the greatest SUID across safe AND unsafe — whichever is higher
        // is where we should resume. Safe rows that were promoted may have
        // higher SUIDs than any current unsafe row.
        var lastSuid = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            $"""
             SELECT COALESCE(MAX(x.suid), '')
             FROM (
                 SELECT {UnsafeWindowMvColumns.LastSortableUniqueId} AS suid FROM {_resolver.SafeTable}
                 UNION ALL
                 SELECT {UnsafeWindowMvColumns.LastSortableUniqueId} AS suid FROM {_resolver.UnsafeTable}
             ) x
             """,
            cancellationToken: ct)).ConfigureAwait(false);

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

        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var serialized in events)
        {
            ct.ThrowIfCancellationRequested();
            var evResult = serialized.ToEvent(_eventTypes);
            if (!evResult.IsSuccess)
            {
                throw evResult.GetException();
            }

            await _applier.ApplyEventAsync(connection, transaction, evResult.GetValue(), ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        _logger.LogDebug("Unsafe window MV '{View}' caught up {Count} event(s).", _projector.ViewName, events.Count);
        return events.Count;
    }
}

/// <summary>
///     BackgroundService that loops the catch-up + promotion cycle forever.
///     Owns a single projector instance; register one per projector type.
/// </summary>
public sealed class UnsafeWindowMvHostedService<TRow> : BackgroundService where TRow : class
{
    private readonly UnsafeWindowMvInitializer _initializer;
    private readonly UnsafeWindowMvCatchUpWorker<TRow> _catchUp;
    private readonly UnsafeWindowMvPromoter<TRow> _promoter;
    private readonly TimeSpan _idleDelay;
    private readonly ILogger<UnsafeWindowMvHostedService<TRow>> _logger;
    private readonly string _viewName;

    public UnsafeWindowMvHostedService(
        UnsafeWindowMvInitializer initializer,
        UnsafeWindowMvCatchUpWorker<TRow> catchUp,
        UnsafeWindowMvPromoter<TRow> promoter,
        string viewName,
        ILogger<UnsafeWindowMvHostedService<TRow>> logger,
        TimeSpan? idleDelay = null)
    {
        _initializer = initializer;
        _catchUp = catchUp;
        _promoter = promoter;
        _viewName = viewName;
        _logger = logger;
        _idleDelay = idleDelay ?? TimeSpan.FromSeconds(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _initializer.InitializeAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unsafe window MV '{View}' initialization failed; the background service will retry on the next iteration.", _viewName);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var did = 0;
            try
            {
                did += await _catchUp.CatchUpOnceAsync(stoppingToken).ConfigureAwait(false);
                did += await _promoter.PromoteOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unsafe window MV '{View}' iteration failed; retrying after {Delay}.", _viewName, _idleDelay);
            }

            if (did == 0)
            {
                try
                {
                    await Task.Delay(_idleDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
