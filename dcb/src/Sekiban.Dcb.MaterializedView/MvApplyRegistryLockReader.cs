using System.Data;
using System.Data.Common;

namespace Sekiban.Dcb.MaterializedView;

/// <summary>One ordered apply lock read, with provider-specific locking and row normalization.</summary>
internal static class MvApplyRegistryLockReader
{
    // Compose provider SQL only from constants; all registry identity values remain parameters.
    internal const string RegistrySelectPrefix = """
        SELECT service_id AS ServiceId,
        view_name AS ViewName,
        view_version AS ViewVersion,
        logical_table AS LogicalTable,
        physical_table AS PhysicalTable,
        status AS Status,
        current_position AS CurrentPosition,
        target_position AS TargetPosition,

        """;

    internal const string RegistrySelectSuffix = """
        last_sortable_unique_id AS LastSortableUniqueId,
        applied_event_version AS AppliedEventVersion,
        last_applied_source AS LastAppliedSource,
        last_applied_at AS LastAppliedAt,
        last_stream_received_sortable_unique_id AS LastStreamReceivedSortableUniqueId,
        last_stream_received_at AS LastStreamReceivedAt,
        last_stream_applied_sortable_unique_id AS LastStreamAppliedSortableUniqueId,
        last_catch_up_sortable_unique_id AS LastCatchUpSortableUniqueId,
        last_updated AS LastUpdated,

        """;

    internal const string RegistryFilterAndOrder = """

        WHERE service_id = @ServiceId
        AND view_name = @ViewName
        AND view_version = @ViewVersion
        ORDER BY logical_table
        """;

    private const string RegistryFrom = " FROM sekiban_mv_registry";
    private const string NativeRegistrySelect = RegistrySelectPrefix +
        "current_checkpoint_truth AS CurrentCheckpointTruth, target_checkpoint_truth AS TargetCheckpointTruth, " +
        RegistrySelectSuffix + "metadata AS Metadata";
    private const string PostgresRegistrySelect = RegistrySelectPrefix +
        "current_checkpoint_truth::text AS CurrentCheckpointTruth, target_checkpoint_truth::text AS TargetCheckpointTruth, " +
        RegistrySelectSuffix + "metadata::text AS Metadata";
    private const string PostgresRegistryRead = PostgresRegistrySelect + RegistryFrom + RegistryFilterAndOrder;

    internal const string PostgresRegistryEntriesSql = PostgresRegistryRead + ";";
    internal const string PostgresApplyLockSql = PostgresRegistryRead + " FOR UPDATE;";
    internal const string SqlServerRegistryEntriesSql = NativeRegistrySelect + RegistryFrom + RegistryFilterAndOrder + ";";
    internal const string SqlServerApplyLockSql = NativeRegistrySelect + RegistryFrom +
        " WITH (UPDLOCK, HOLDLOCK)" + RegistryFilterAndOrder + ";";

    internal sealed record ReadOptions(
        string Sql, string UnlockedSql, string? FenceSql = null, bool AllowStringTimestamps = false);

    internal static Task<IReadOnlyList<MvRegistryEntry>> ReadAsync(
        string serviceId, string viewName, int viewVersion, IDbTransaction transaction,
        CancellationToken cancellationToken, ReadOptions options) =>
        ReadAsync(serviceId, viewName, viewVersion, transaction,
            row => MapEntry(row, options.AllowStringTimestamps), cancellationToken, options);

    internal static async Task<IReadOnlyList<MvRegistryEntry>> ReadAsync(
        string serviceId, string viewName, int viewVersion, IDbTransaction transaction,
        Func<IReadOnlyDictionary<string, object?>, MvRegistryEntry> mapRow,
        CancellationToken cancellationToken, ReadOptions options)
    {
        var connection = (DbConnection)(transaction.Connection
            ?? throw new InvalidOperationException("The apply transaction has no connection."));
        await MvLifecycleTestHooks.InvokeBeforeApplyLockReadAsync(connection, cancellationToken).ConfigureAwait(false);
        if (MvLifecycleTestHooks.ApplyLockReadOverride is { } readOverride)
            return await readOverride(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (DbTransaction)transaction;
        AddParameter(command, "ServiceId", serviceId);
        AddParameter(command, "ViewName", viewName);
        AddParameter(command, "ViewVersion", viewVersion);
        if (!MvLifecycleTestHooks.ApplyLockDisabled && options.FenceSql is not null)
        {
            command.CommandText = options.FenceSql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        command.CommandText = MvLifecycleTestHooks.ApplyLockDisabled ? options.UnlockedSql : options.Sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<MvRegistryEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            entries.Add(mapRow(row));
        }
        return entries;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    internal static MvRegistryEntry MapEntry(
        IReadOnlyDictionary<string, object?> row, bool allowStringTimestamps = false)
    {
        var values = new RegistryRow(row, allowStringTimestamps);
        var currentCheckpointTruth = MvCheckpointTruthCodec.Decode(values.NullableString("CurrentCheckpointTruth"));
        var targetCheckpointTruth = MvCheckpointTruthCodec.Decode(values.NullableString("TargetCheckpointTruth"));
        return new()
        {
            ServiceId = values.RequiredString("ServiceId"),
            ViewName = values.RequiredString("ViewName"),
            ViewVersion = values.RequiredInt("ViewVersion"),
            LogicalTable = values.RequiredString("LogicalTable"),
            PhysicalTable = values.RequiredString("PhysicalTable"),
            Status = Enum.Parse<MvStatus>(values.RequiredString("Status"), ignoreCase: true),
            CurrentPosition = values.NullableString("CurrentPosition") ?? currentCheckpointTruth.PositionValue,
            TargetPosition = values.NullableString("TargetPosition") ?? targetCheckpointTruth.PositionValue,
            CurrentCheckpointTruth = currentCheckpointTruth,
            TargetCheckpointTruth = targetCheckpointTruth,
            LastSortableUniqueId = values.NullableString("LastSortableUniqueId"),
            AppliedEventVersion = values.RequiredLong("AppliedEventVersion"),
            LastAppliedSource = values.NullableString("LastAppliedSource"),
            LastAppliedAt = values.NullableTimestamp("LastAppliedAt"),
            LastStreamReceivedSortableUniqueId = values.NullableString("LastStreamReceivedSortableUniqueId"),
            LastStreamReceivedAt = values.NullableTimestamp("LastStreamReceivedAt"),
            LastStreamAppliedSortableUniqueId = values.NullableString("LastStreamAppliedSortableUniqueId"),
            LastCatchUpSortableUniqueId = values.NullableString("LastCatchUpSortableUniqueId"),
            LastUpdated = values.RequiredTimestamp("LastUpdated"),
            Metadata = values.NullableString("Metadata")
        };
    }

    private readonly struct RegistryRow(IReadOnlyDictionary<string, object?> row, bool allowStringTimestamps)
    {
        internal string RequiredString(string key) =>
            GetRequiredValue(key) is { } value ? value.ToString()! : throw MissingValue(key);

        internal string? NullableString(string key) => GetValue(key)?.ToString();
        internal int RequiredInt(string key) => Convert.ToInt32(GetRequiredValue(key));
        internal long RequiredLong(string key) => Convert.ToInt64(GetRequiredValue(key));
        internal DateTimeOffset? NullableTimestamp(string key) => ReadTimestamp(GetValue(key), key);
        internal DateTimeOffset RequiredTimestamp(string key) =>
            ReadTimestamp(GetRequiredValue(key), key) ??
            throw new InvalidOperationException($"Registry row is missing required timestamp '{key}'.");

        private object? GetRequiredValue(string key) =>
            TryRead(key, out var value) ? value : throw MissingValue(key);
        private object? GetValue(string key) => TryRead(key, out var value) ? value : null;
        private static InvalidOperationException MissingValue(string key) =>
            new($"Registry row is missing required value '{key}'.");

        private bool TryRead(string key, out object? value)
        {
            if (row.TryGetValue(key, out value)) return true;
            foreach (var pair in row)
            {
                if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
                value = pair.Value;
                return true;
            }
            value = null;
            return false;
        }

        private DateTimeOffset? ReadTimestamp(object? value, string key) => value switch
        {
            null or DBNull => null,
            DateTimeOffset offset => offset,
            DateTime timestamp => new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
            string text when allowStringTimestamps && DateTimeOffset.TryParse(text, out var parsed) => parsed,
            _ => throw new InvalidOperationException($"Registry row value '{key}' must be a timestamp.")
        };
    }
}
