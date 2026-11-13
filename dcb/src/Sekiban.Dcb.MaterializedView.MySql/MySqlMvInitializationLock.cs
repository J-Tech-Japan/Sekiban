using System.Data;
using Dapper;
using MySqlConnector;

namespace Sekiban.Dcb.MaterializedView.MySql;

internal sealed class MySqlMvInitializationLock(MySqlConnection connection, IDbTransaction? transaction) : IAsyncDisposable
{
    // Fixed server-wide name: conservatively serializes all MV databases on this server.
    // MySQL DDL commits implicitly, so a session lock must span inspection and all ALTER statements.
    private const string Name = "sekiban_mv_initialization";

    internal static async Task<MySqlMvInitializationLock> AcquireAsync(
        MySqlConnection connection, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        var acquired = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT GET_LOCK(@Name, 60);", new { Name }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (acquired != 1) throw new TimeoutException("Could not acquire MV initialization lock.");
        return new MySqlMvInitializationLock(connection, transaction);
    }

    public async ValueTask DisposeAsync()
    {
        // Always release before this session returns to the pool, including after cancellation.
        await connection.ExecuteAsync(new CommandDefinition(
            "DO RELEASE_LOCK(@Name);", new { Name }, transaction?.Connection is null ? null : transaction)).ConfigureAwait(false);
    }
}
