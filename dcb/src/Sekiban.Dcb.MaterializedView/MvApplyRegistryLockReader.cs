using System.Data;
using System.Data.Common;

namespace Sekiban.Dcb.MaterializedView;

/// <summary>One ordered apply lock read, with provider-specific locking and row normalization.</summary>
internal static class MvApplyRegistryLockReader
{
    internal sealed record ReadOptions(string Sql, string UnlockedSql, string? FenceSql = null);

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
}
