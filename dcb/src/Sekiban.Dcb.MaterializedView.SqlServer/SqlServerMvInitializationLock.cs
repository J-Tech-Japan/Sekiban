using System.Data;
using Dapper;

namespace Sekiban.Dcb.MaterializedView.SqlServer;

internal static class SqlServerMvInitializationLock
{
    // Fixed database-wide initialization application resource; shared by registry and projector DDL.
    // Transaction ownership releases the lock on commit, rollback, cancellation or connection loss.
    internal const string Sql = """
        DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = N'sekiban_mv_initialization',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 60000;
            IF @result < 0 THROW 51000, 'Could not acquire MV initialization lock', 1;
        """;

    internal static Task AcquireAsync(IDbConnection connection, IDbTransaction transaction, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(Sql, transaction: transaction, cancellationToken: cancellationToken));
}
