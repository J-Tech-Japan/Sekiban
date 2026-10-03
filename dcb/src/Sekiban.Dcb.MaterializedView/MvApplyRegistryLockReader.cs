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
        var currentCheckpointTruth = MvCheckpointTruthCodec.Decode(ReadNullableString(row, "CurrentCheckpointTruth"));
        var targetCheckpointTruth = MvCheckpointTruthCodec.Decode(ReadNullableString(row, "TargetCheckpointTruth"));
        return new()
        {
            ServiceId = ReadRequiredString(row, "ServiceId"),
            ViewName = ReadRequiredString(row, "ViewName"),
            ViewVersion = ReadRequiredInt(row, "ViewVersion"),
            LogicalTable = ReadRequiredString(row, "LogicalTable"),
            PhysicalTable = ReadRequiredString(row, "PhysicalTable"),
            Status = Enum.Parse<MvStatus>(ReadRequiredString(row, "Status"), ignoreCase: true),
            CurrentPosition = ReadNullableString(row, "CurrentPosition") ?? currentCheckpointTruth.PositionValue,
            TargetPosition = ReadNullableString(row, "TargetPosition") ?? targetCheckpointTruth.PositionValue,
            CurrentCheckpointTruth = currentCheckpointTruth,
            TargetCheckpointTruth = targetCheckpointTruth,
            LastSortableUniqueId = ReadNullableString(row, "LastSortableUniqueId"),
            AppliedEventVersion = ReadRequiredLong(row, "AppliedEventVersion"),
            LastAppliedSource = ReadNullableString(row, "LastAppliedSource"),
            LastAppliedAt = ReadNullableDateTimeOffset(row, "LastAppliedAt", allowStringTimestamps),
            LastStreamReceivedSortableUniqueId = ReadNullableString(row, "LastStreamReceivedSortableUniqueId"),
            LastStreamReceivedAt = ReadNullableDateTimeOffset(row, "LastStreamReceivedAt", allowStringTimestamps),
            LastStreamAppliedSortableUniqueId = ReadNullableString(row, "LastStreamAppliedSortableUniqueId"),
            LastCatchUpSortableUniqueId = ReadNullableString(row, "LastCatchUpSortableUniqueId"),
            LastUpdated = ReadRequiredDateTimeOffset(row, "LastUpdated", allowStringTimestamps),
            Metadata = ReadNullableString(row, "Metadata")
        };
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

    private static DateTimeOffset ReadRequiredDateTimeOffset(IReadOnlyDictionary<string, object?> row, string key, bool allowStringTimestamps) =>
        ReadDateTimeOffsetCore(
            TryGetValue(row, key, out var value)
                ? value
                : throw new InvalidOperationException($"Registry row is missing required value '{key}'."),
            key, allowStringTimestamps) ??
        throw new InvalidOperationException($"Registry row is missing required timestamp '{key}'.");

    private static DateTimeOffset? ReadNullableDateTimeOffset(IReadOnlyDictionary<string, object?> row, string key, bool allowStringTimestamps) =>
        TryGetValue(row, key, out var value) ? ReadDateTimeOffsetCore(value, key, allowStringTimestamps) : null;

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

    private static DateTimeOffset? ReadDateTimeOffsetCore(object? value, string key, bool allowStringTimestamps) =>
        value switch
        {
            null or DBNull => null,
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => Normalize(dateTime),
            string text when allowStringTimestamps && DateTimeOffset.TryParse(text, out var parsed) => parsed,
            _ => throw new InvalidOperationException($"Registry row value '{key}' must be a timestamp.")
        };

    private static DateTimeOffset Normalize(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
