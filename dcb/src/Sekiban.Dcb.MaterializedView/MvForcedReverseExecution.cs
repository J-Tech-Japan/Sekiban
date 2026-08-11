using System.Data.Common;

namespace Sekiban.Dcb.MaterializedView;

/// <summary>Provider SQL used by the shared forced-reverse transaction mechanics.</summary>
public sealed record MvForcedReverseSqlPlan(
    string CandidateFenceSql,
    bool FenceReturnsRows,
    string CandidateCountSql,
    string PointerCasSql,
    string MarkPreviousReadySql,
    string MarkCandidateActiveSql,
    string SavepointSql,
    string RollbackSavepointSql,
    string? ReleaseSavepointSql)
{
    private const string CommonCandidateCountSql = """
        SELECT COUNT(*) FROM sekiban_mv_registry
        WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ViewVersion
          AND status = @ExpectedStatus;
        """;

    private const string CommonPointerCasSql = """
        UPDATE sekiban_mv_active
        SET active_version = @ViewVersion,
            active_generation = active_generation + 1,
            activated_at = @RequestedAtUtc,
            switch_kind = 'forced',
            switch_reason = @Reason,
            switched_at_utc = @RequestedAtUtc
        WHERE service_id = @ServiceId AND view_name = @ViewName
          AND active_version = @ExpectedActiveVersion
          AND active_generation = @ExpectedActiveGeneration;
        """;

    private const string CommonMarkPreviousReadySql = """
        UPDATE sekiban_mv_registry SET status = 'ready', last_updated = @RequestedAtUtc
        WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ExpectedActiveVersion;
        """;

    private const string CommonMarkCandidateActiveSql = """
        UPDATE sekiban_mv_registry SET status = 'active', last_updated = @RequestedAtUtc
        WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ViewVersion
          AND status = @ExpectedStatus;
        """;

    public static MvForcedReverseSqlPlan Create(
        string candidateFenceSql,
        bool fenceReturnsRows,
        string savepointSql,
        string rollbackSavepointSql,
        string? releaseSavepointSql,
        string? pointerCasSql = null) =>
        new(
            candidateFenceSql,
            fenceReturnsRows,
            CommonCandidateCountSql,
            pointerCasSql ?? CommonPointerCasSql,
            CommonMarkPreviousReadySql,
            CommonMarkCandidateActiveSql,
            savepointSql,
            rollbackSavepointSql,
            releaseSavepointSql);
}

/// <summary>
///     Shared non-resolution mechanics for forced reverse. Providers still own connections and static SQL; this helper
///     only enforces transaction, savepoint, exact-row-count, and rollback behavior.
/// </summary>
public static class MvForcedReverseExecution
{
    public static async Task<MvActivationResult> ExecuteAsync<TConnection>(
        MvForcedReverseRequest request,
        System.Data.IDbTransaction? callerTransaction,
        Func<TConnection> createConnection,
        MvForcedReverseSqlPlan sql,
        object requestedAtValue,
        CancellationToken cancellationToken)
        where TConnection : DbConnection
    {
        var validation = MvForcedReverseValidation.Validate(request);
        if (validation is not null)
        {
            return validation;
        }

        if (callerTransaction is not null)
        {
            return await ExecuteWithSavepointAsync(
                    RequireDbTransaction(callerTransaction),
                    request,
                    sql,
                    requestedAtValue,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await using var connection = createConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var result = await ExecuteInTransactionAsync(
                transaction,
                request,
                sql,
                requestedAtValue,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private static async Task<MvActivationResult> ExecuteWithSavepointAsync(
        DbTransaction transaction,
        MvForcedReverseRequest request,
        MvForcedReverseSqlPlan sql,
        object requestedAtValue,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(transaction, sql.SavepointSql, null, cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await ExecuteInTransactionAsync(
                    transaction,
                    request,
                    sql,
                    requestedAtValue,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                await ExecuteNonQueryAsync(transaction, sql.RollbackSavepointSql, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (sql.ReleaseSavepointSql is not null)
            {
                await ExecuteNonQueryAsync(transaction, sql.ReleaseSavepointSql, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            return result;
        }
        catch
        {
            await ExecuteNonQueryAsync(transaction, sql.RollbackSavepointSql, null, cancellationToken)
                .ConfigureAwait(false);
            if (sql.ReleaseSavepointSql is not null)
            {
                await ExecuteNonQueryAsync(transaction, sql.ReleaseSavepointSql, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            throw;
        }
    }

    private static async Task<MvActivationResult> ExecuteInTransactionAsync(
        DbTransaction transaction,
        MvForcedReverseRequest request,
        MvForcedReverseSqlPlan sql,
        object requestedAtValue,
        CancellationToken cancellationToken)
    {
        var parameters = Parameters(request, requestedAtValue);
        var fenced = sql.FenceReturnsRows
            ? await CountRowsAsync(transaction, sql.CandidateFenceSql, parameters, cancellationToken).ConfigureAwait(false)
            : await ExecuteNonQueryAsync(transaction, sql.CandidateFenceSql, parameters, cancellationToken).ConfigureAwait(false);
        var matching = Convert.ToInt32(
            await ExecuteScalarAsync(transaction, sql.CandidateCountSql, parameters, cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        if (fenced != request.CandidateCount || matching != request.CandidateCount)
        {
            return Conflict();
        }

        if (await ExecuteNonQueryAsync(transaction, sql.PointerCasSql, parameters, cancellationToken).ConfigureAwait(false) != 1)
        {
            return Conflict();
        }

        await ExecuteNonQueryAsync(transaction, sql.MarkPreviousReadySql, parameters, cancellationToken)
            .ConfigureAwait(false);
        var marked = await ExecuteNonQueryAsync(
                transaction,
                sql.MarkCandidateActiveSql,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return marked == request.CandidateCount
            ? MvActivationResult.Success(request.ExpectedActiveGeneration + 1)
            : Conflict();
    }

    private static async Task<int> CountRowsAsync(
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

    private static async Task<int> ExecuteNonQueryAsync(
        DbTransaction transaction,
        string sql,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> ExecuteScalarAsync(
        DbTransaction transaction,
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(transaction, sql, parameters);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DbCommand CreateCommand(
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

    private static IReadOnlyDictionary<string, object?> Parameters(
        MvForcedReverseRequest request,
        object requestedAtValue) => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ServiceId"] = request.ServiceId,
            ["ViewName"] = request.ViewName,
            ["ViewVersion"] = request.ViewVersion,
            ["ExpectedActiveVersion"] = request.ExpectedActiveVersion,
            ["ExpectedActiveGeneration"] = request.ExpectedActiveGeneration,
            ["ExpectedStatus"] = request.ExpectedStatus.ToString().ToLowerInvariant(),
            ["Reason"] = request.Reason,
            ["RequestedAtUtc"] = requestedAtValue
        };

    private static DbTransaction RequireDbTransaction(System.Data.IDbTransaction transaction) =>
        transaction as DbTransaction ??
        throw new ArgumentException("The caller transaction must derive from DbTransaction.", nameof(transaction));

    private static MvActivationResult Conflict() => MvActivationResult.Rejected(
        MvActivationFailureReason.ConcurrentSuperseded,
        "The forced-reverse identity, pointer fence, lifecycle, or candidate snapshot changed.");
}

/// <summary>Infrastructure base that keeps the provider entry point free of repeated transaction mechanics.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public abstract class MvForcedReverseRegistryStoreBase<TConnection>
    where TConnection : DbConnection
{
    protected abstract MvForcedReverseSqlPlan ForcedReversePlan { get; }
    protected abstract TConnection CreateForcedReverseConnection();
    protected virtual object FormatForcedReverseTimestamp(DateTimeOffset value) => value;

    public Task<MvActivationResult> TryForceReverseAsync(
        MvForcedReverseRequest request,
        System.Data.IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default) =>
        MvForcedReverseExecution.ExecuteAsync(
            request,
            transaction,
            CreateForcedReverseConnection,
            ForcedReversePlan,
            FormatForcedReverseTimestamp(request.RequestedAtUtc),
            cancellationToken);
}
