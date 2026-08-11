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

/// <summary>Provider SQL for audit metadata written inside an ordinary activation transaction.</summary>
public sealed record MvOrdinarySwitchAuditSqlPlan(
    string PersistActiveAuditSql,
    string MarkPreviousReadySql)
{
    public static MvOrdinarySwitchAuditSqlPlan Common { get; } = new(
        """
        UPDATE sekiban_mv_active
        SET switch_kind = @SwitchKind, switch_reason = NULL, switched_at_utc = @SwitchedAtUtc
        WHERE service_id = @ServiceId AND view_name = @ViewName AND active_version = @ViewVersion
          AND active_generation = @ActivatedGeneration;
        """,
        """
        UPDATE sekiban_mv_registry SET status = 'ready', last_updated = @SwitchedAtUtc
        WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ExpectedActiveVersion;
        """);
}

/// <summary>
///     Shared non-resolution mechanics for forced reverse. Providers still own connections and static SQL; this helper
///     only enforces transaction, savepoint, exact-row-count, and rollback behavior.
/// </summary>
public static class MvForcedReverseExecution
{
    private const string LegacySwitchAuditSql = """
        UPDATE sekiban_mv_active
        SET switch_kind = 'legacy', switch_reason = NULL, switched_at_utc = @SwitchedAtUtc
        WHERE service_id = @ServiceId AND view_name = @ViewName AND active_version = @ActiveVersion;
        """;

    internal static async Task SetLegacyActiveAsync<TConnection>(
        string serviceId,
        string viewName,
        int activeVersion,
        System.Data.IDbTransaction? callerTransaction,
        Func<TConnection> createConnection,
        string pointerUpsertSql,
        object switchedAtValue,
        CancellationToken cancellationToken)
        where TConnection : DbConnection
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ServiceId"] = serviceId,
            ["ViewName"] = viewName,
            ["ActiveVersion"] = activeVersion,
            ["SwitchedAtUtc"] = switchedAtValue
        };
        if (callerTransaction is not null)
        {
            await SetLegacyActiveInTransactionAsync(
                RequireDbTransaction(callerTransaction),
                pointerUpsertSql,
                parameters,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var connection = createConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SetLegacyActiveInTransactionAsync(
                transaction,
                pointerUpsertSql,
                parameters,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task SetLegacyActiveInTransactionAsync(
        DbTransaction transaction,
        string pointerUpsertSql,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(transaction, pointerUpsertSql, parameters, cancellationToken).ConfigureAwait(false);
        if (await ExecuteNonQueryAsync(
                transaction,
                LegacySwitchAuditSql,
                parameters,
                cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("The legacy active pointer changed before its switch audit was persisted.");
        }
    }

    internal static async Task PersistOrdinarySwitchAuditAsync(
        System.Data.IDbTransaction transaction,
        MvActivationRequest request,
        MvOrdinarySwitchAuditSqlPlan sql,
        object switchedAtValue,
        CancellationToken cancellationToken)
    {
        var dbTransaction = RequireDbTransaction(transaction);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ServiceId"] = request.ServiceId,
            ["ViewName"] = request.ViewName,
            ["ViewVersion"] = request.ViewVersion,
            ["ExpectedActiveVersion"] = request.ExpectedActiveVersion,
            ["ActivatedGeneration"] = request.ExpectedActiveGeneration + 1,
            ["SwitchKind"] = request.SwitchKind.ToString().ToLowerInvariant(),
            ["SwitchedAtUtc"] = switchedAtValue
        };
        if (await ExecuteNonQueryAsync(
                dbTransaction,
                sql.PersistActiveAuditSql,
                parameters,
                cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("The activated pointer changed before its switch audit was persisted.");
        }

        if (request.ExpectedActiveVersion is not null)
        {
            await ExecuteNonQueryAsync(
                    dbTransaction,
                    sql.MarkPreviousReadySql,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

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
    protected abstract string LegacySetActiveSql { get; }
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

    protected Task SetLegacyActiveAsync(
        string serviceId,
        string viewName,
        int activeVersion,
        System.Data.IDbTransaction? transaction,
        CancellationToken cancellationToken) =>
        MvForcedReverseExecution.SetLegacyActiveAsync(
            serviceId,
            viewName,
            activeVersion,
            transaction,
            CreateForcedReverseConnection,
            LegacySetActiveSql,
            FormatForcedReverseTimestamp(DateTimeOffset.UtcNow),
            cancellationToken);

    protected Task PersistOrdinarySwitchAuditAsync(
        System.Data.IDbTransaction transaction,
        MvActivationRequest request,
        CancellationToken cancellationToken) =>
        MvForcedReverseExecution.PersistOrdinarySwitchAuditAsync(
            transaction,
            request,
            MvOrdinarySwitchAuditSqlPlan.Common,
            FormatForcedReverseTimestamp(DateTimeOffset.UtcNow),
            cancellationToken);

    protected static async Task EnsureMissingActiveColumnsAsync(
        DbConnection connection,
        IReadOnlySet<string> existingColumns,
        CancellationToken cancellationToken,
        params (string Name, string Sql)[] columns)
    {
        foreach (var column in columns)
        {
            if (existingColumns.Contains(column.Name))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = column.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    protected static MvActiveEntry ReadActiveEntry(IReadOnlyDictionary<string, object?> row)
    {
        var switchKindText = ReadNullableString(row, "SwitchKind");
        return new MvActiveEntry(
            ReadRequiredString(row, "ServiceId"),
            ReadRequiredString(row, "ViewName"),
            Convert.ToInt32(ReadRequired(row, "ActiveVersion"), System.Globalization.CultureInfo.InvariantCulture),
            ReadRequiredTimestamp(row, "ActivatedAt"))
        {
            Generation = Convert.ToInt64(ReadRequired(row, "Generation"), System.Globalization.CultureInfo.InvariantCulture),
            SwitchKind = Enum.TryParse<MvSwitchKind>(switchKindText, true, out var kind) ? kind : MvSwitchKind.Legacy,
            SwitchReason = ReadNullableString(row, "SwitchReason"),
            SwitchedAtUtc = ReadNullableTimestamp(row, "SwitchedAtUtc")
        };
    }

    private static string ReadRequiredString(IReadOnlyDictionary<string, object?> row, string key) =>
        ReadRequired(row, key).ToString() ??
        throw new InvalidOperationException($"Registry row value '{key}' cannot be converted to a string.");

    private static string? ReadNullableString(IReadOnlyDictionary<string, object?> row, string key)
    {
        var value = Read(row, key);
        return value is null or DBNull ? null : value.ToString();
    }

    private static DateTimeOffset ReadRequiredTimestamp(IReadOnlyDictionary<string, object?> row, string key) =>
        ReadTimestamp(ReadRequired(row, key), key);

    private static DateTimeOffset? ReadNullableTimestamp(IReadOnlyDictionary<string, object?> row, string key)
    {
        var value = Read(row, key);
        return value is null or DBNull ? null : ReadTimestamp(value, key);
    }

    private static DateTimeOffset ReadTimestamp(object value, string key) => value switch
    {
        DateTimeOffset dateTimeOffset => dateTimeOffset,
        DateTime dateTime => new DateTimeOffset(
            dateTime.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                : dateTime),
        string text when DateTimeOffset.TryParse(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed) => parsed,
        _ => throw new InvalidOperationException($"Registry row value '{key}' is not a timestamp.")
    };

    private static object ReadRequired(IReadOnlyDictionary<string, object?> row, string key) =>
        Read(row, key) is { } value && value is not DBNull
            ? value
            : throw new InvalidOperationException($"Registry row is missing required value '{key}'.");

    private static object? Read(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out var value))
        {
            return value;
        }

        foreach (var pair in row)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
