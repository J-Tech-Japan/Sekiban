using System.Data.Common;

namespace Sekiban.Dcb.MaterializedView;

internal static class MvDbCommandHelper
{
    internal static async Task<int> CountRowsAsync(
        DbTransaction transaction,
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(transaction, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var count = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            count++;
        }

        return count;
    }

    internal static async Task<int> ExecuteNonQueryAsync(
        DbTransaction transaction,
        string sql,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static DbCommand CreateCommand(
        DbTransaction transaction,
        string sql,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        var connection = transaction.Connection ??
            throw new InvalidOperationException("The transaction is not associated with a connection.");
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (parameters is null)
        {
            return command;
        }

        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }
}
