using System.Data;
using Dapper;

namespace Sekiban.Dcb.MaterializedView.Postgres;

internal static class PostgresMvInitializationLock
{
    // Fixed database-wide initialization advisory key; shared by registry and projector DDL.
    // Transaction ownership releases the lock on commit, rollback, cancellation or connection loss.
    internal const string Sql = """
        SELECT pg_advisory_xact_lock(76423910614243);
        """;

    internal static Task AcquireAsync(IDbConnection connection, IDbTransaction transaction, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(Sql, transaction: transaction, cancellationToken: cancellationToken));
}
