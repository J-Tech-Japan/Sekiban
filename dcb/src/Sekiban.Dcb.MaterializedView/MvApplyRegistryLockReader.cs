using System.Data;
using System.Data.Common;

namespace Sekiban.Dcb.MaterializedView;

/// <summary>One ordered apply lock read, with provider-specific locking and row normalization.</summary>
internal static class MvApplyRegistryLockReader
{
    internal static async Task<IReadOnlyList<MvRegistryEntry>> ReadAsync(
        string serviceId, string viewName, int viewVersion, IDbTransaction transaction,
        Func<IReadOnlyDictionary<string, object?>, MvRegistryEntry> mapRow,
        CancellationToken cancellationToken, string tableHint = "", string suffix = "",
        string jsonCast = "", string? fenceSql = null)
    {
        var connection = (DbConnection)(transaction.Connection
            ?? throw new InvalidOperationException("The apply transaction has no connection."));
        await MvLifecycleTestHooks.InvokeBeforeApplyLockReadAsync(connection, cancellationToken).ConfigureAwait(false);
        if (MvLifecycleTestHooks.ApplyLockReadOverride is { } readOverride)
            return await readOverride(cancellationToken).ConfigureAwait(false);
        if (MvLifecycleTestHooks.ApplyLockDisabled)
        {
            tableHint = string.Empty;
            suffix = string.Empty;
            fenceSql = null;
        }
        await using var command = connection.CreateCommand();
        command.Transaction = (DbTransaction)transaction;
        AddParameter(command, "ServiceId", serviceId);
        AddParameter(command, "ViewName", viewName);
        AddParameter(command, "ViewVersion", viewVersion);
        if (fenceSql is not null)
        {
            command.CommandText = fenceSql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        command.CommandText = $"""
            SELECT {Projection(jsonCast)}
            FROM sekiban_mv_registry {tableHint}
            WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ViewVersion
            ORDER BY logical_table {suffix};
            """;
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

    // Generate the aliases once instead of duplicating each provider's full SELECT projection.
    private static readonly (string Column, string Alias, bool Json)[] Columns =
    [
        ("service_id", nameof(MvRegistryEntry.ServiceId), false),
        ("view_name", nameof(MvRegistryEntry.ViewName), false),
        ("view_version", nameof(MvRegistryEntry.ViewVersion), false),
        ("logical_table", nameof(MvRegistryEntry.LogicalTable), false),
        ("physical_table", nameof(MvRegistryEntry.PhysicalTable), false),
        ("status", nameof(MvRegistryEntry.Status), false),
        ("current_position", nameof(MvRegistryEntry.CurrentPosition), false),
        ("target_position", nameof(MvRegistryEntry.TargetPosition), false),
        ("current_checkpoint_truth", nameof(MvRegistryEntry.CurrentCheckpointTruth), true),
        ("target_checkpoint_truth", nameof(MvRegistryEntry.TargetCheckpointTruth), true),
        ("last_sortable_unique_id", nameof(MvRegistryEntry.LastSortableUniqueId), false),
        ("applied_event_version", nameof(MvRegistryEntry.AppliedEventVersion), false),
        ("last_applied_source", nameof(MvRegistryEntry.LastAppliedSource), false),
        ("last_applied_at", nameof(MvRegistryEntry.LastAppliedAt), false),
        ("last_stream_received_sortable_unique_id", nameof(MvRegistryEntry.LastStreamReceivedSortableUniqueId), false),
        ("last_stream_received_at", nameof(MvRegistryEntry.LastStreamReceivedAt), false),
        ("last_stream_applied_sortable_unique_id", nameof(MvRegistryEntry.LastStreamAppliedSortableUniqueId), false),
        ("last_catch_up_sortable_unique_id", nameof(MvRegistryEntry.LastCatchUpSortableUniqueId), false),
        ("last_updated", nameof(MvRegistryEntry.LastUpdated), false),
        ("metadata", nameof(MvRegistryEntry.Metadata), true),
    ];

    private static string Projection(string jsonCast) => string.Join(", ",
        Columns.Select(column => $"{column.Column}{(column.Json ? jsonCast : string.Empty)} AS {column.Alias}"));

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
