using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Sekiban.Dcb.MaterializedView;

/// <summary>
///     Provider-owned SQL and transaction syntax for restoring the status of a serving materialized-view version.
///     The candidate query must lock every registry row in deterministic logical-table order before the active-pointer
///     query is executed. SQLite uses a write fence because it has no row-level FOR UPDATE primitive.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed record MvActiveStatusRestoreSqlPlan(
    string CandidateLockSql,
    string? CandidateFenceSql,
    bool CandidateFenceReturnsRows,
    string ActivePointerLockSql,
    string RestoreStatusesSql,
    string SavepointSql,
    string RollbackSavepointSql,
    string? ReleaseSavepointSql);

/// <summary>Raised when a caller-owned transaction cannot be returned to the operation savepoint.</summary>
public sealed class MvActiveStatusRestoreTransactionAbortedException : InvalidOperationException
{
    public MvActiveStatusRestoreTransactionAbortedException(Exception innerException)
        : base("The caller transaction could not be rolled back to the serving-status restore savepoint.", innerException)
    {
    }
}

/// <summary>
///     Shared provider-neutral mechanics for the additive serving-status repair. Providers supply only static SQL and
///     a connection factory; all identity, truth, lock ordering, rollback, and finite retry rules live here.
/// </summary>
public static class MvActiveStatusRestoreExecution
{
    private const string SavepointOperationName = "serving-status restore";
    private static readonly AsyncLocal<Func<DbTransaction, CancellationToken, Task>?> AfterCandidateLockTestBarrier = new();
    private static readonly AsyncLocal<Func<DbTransaction, CancellationToken, Task>?> AfterActivePointerReadTestBarrier = new();

    internal static IDisposable PushAfterCandidateLockTestBarrier(
        Func<DbTransaction, CancellationToken, Task> barrier)
    {
        ArgumentNullException.ThrowIfNull(barrier);
        var previous = AfterCandidateLockTestBarrier.Value;
        AfterCandidateLockTestBarrier.Value = barrier;
        return new TestBarrierScope(() => AfterCandidateLockTestBarrier.Value = previous);
    }

    internal static IDisposable PushAfterActivePointerReadTestBarrier(
        Func<DbTransaction, CancellationToken, Task> barrier)
    {
        ArgumentNullException.ThrowIfNull(barrier);
        var previous = AfterActivePointerReadTestBarrier.Value;
        AfterActivePointerReadTestBarrier.Value = barrier;
        return new TestBarrierScope(() => AfterActivePointerReadTestBarrier.Value = previous);
    }

    public static async Task<MvActivationResult> ExecuteAsync<TConnection>(
        MvActiveStatusRestoreRequest request,
        System.Data.IDbTransaction? callerTransaction,
        Func<TConnection> createConnection,
        MvActiveStatusRestoreSqlPlan sql,
        object? nowValue,
        CancellationToken cancellationToken)
        where TConnection : DbConnection
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(createConnection);
        ArgumentNullException.ThrowIfNull(sql);

        var validation = MvActiveStatusRestoreValidation.Validate(request);
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
                    nowValue,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        MvActivationResult? lastTransient = null;
        for (var attempt = 1; attempt <= request.MaxAttempts; attempt++)
        {
            try
            {
                await using var connection = createConnection();
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var result = await ExecuteInTransactionAsync(
                            transaction,
                            request,
                            sql,
                            nowValue,
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

                    return result with { AttemptCount = attempt };
                }
                catch
                {
                    // A local transaction is disposable, but explicitly roll it back before the connection is
                    // replaced. A rollback failure must not mask the provider failure that determines retryability.
                    try
                    {
                        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // The next attempt always opens a fresh connection and transaction.
                    }

                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsTransientConcurrency(ex))
            {
                lastTransient = Retryable(attempt);
                if (attempt < request.MaxAttempts)
                {
                    // Each retry loops through a new connection/transaction, so the locked snapshot is read again.
                    continue;
                }

                return RetryExhausted(request.MaxAttempts);
            }
        }

        return lastTransient ?? RetryExhausted(request.MaxAttempts);
    }

    private static async Task<MvActivationResult> ExecuteWithSavepointAsync(
        DbTransaction transaction,
        MvActiveStatusRestoreRequest request,
        MvActiveStatusRestoreSqlPlan sql,
        object? nowValue,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteNonQueryAsync(transaction, sql.SavepointSql, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MvActiveStatusRestoreTransactionAbortedException(ex);
        }

        MvActivationResult result;
        try
        {
            result = await ExecuteInTransactionAsync(
                    transaction,
                    request,
                    sql,
                    nowValue,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await RollbackAndReleaseCallerSavepointOrThrowAsync(transaction, sql).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (IsTransientConcurrency(ex))
        {
            await RollbackAndReleaseCallerSavepointOrThrowAsync(transaction, sql).ConfigureAwait(false);
            return Retryable(1);
        }
        catch
        {
            await RollbackAndReleaseCallerSavepointOrThrowAsync(transaction, sql).ConfigureAwait(false);
            throw;
        }

        if (!result.Succeeded)
        {
            await RollbackAndReleaseCallerSavepointOrThrowAsync(transaction, sql).ConfigureAwait(false);
        }
        else
        {
            try
            {
                await ReleaseCallerSavepointOrThrowAsync(transaction, sql, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await RollbackAndReleaseCallerSavepointOrThrowAsync(transaction, sql).ConfigureAwait(false);
                throw;
            }
            catch
            {
                await RollbackAndReleaseCallerSavepointOrThrowAsync(transaction, sql).ConfigureAwait(false);
                throw;
            }
        }

        return result with { AttemptCount = 1 };
    }

    private static async Task RollbackAndReleaseCallerSavepointOrThrowAsync(
        DbTransaction transaction,
        MvActiveStatusRestoreSqlPlan sql)
    {
        try
        {
            await ExecuteNonQueryAsync(transaction, sql.RollbackSavepointSql, null, CancellationToken.None).ConfigureAwait(false);
            await ReleaseCallerSavepointOrThrowAsync(transaction, sql, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MvActiveStatusRestoreTransactionAbortedException(ex);
        }
    }

    private static async Task ReleaseCallerSavepointOrThrowAsync(
        DbTransaction transaction,
        MvActiveStatusRestoreSqlPlan sql,
        CancellationToken cancellationToken)
    {
        if (sql.ReleaseSavepointSql is null)
        {
            return;
        }

        try
        {
            await ExecuteNonQueryAsync(transaction, sql.ReleaseSavepointSql, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MvActiveStatusRestoreTransactionAbortedException(ex);
        }
    }

    private static async Task<MvActivationResult> ExecuteInTransactionAsync(
        DbTransaction transaction,
        MvActiveStatusRestoreRequest request,
        MvActiveStatusRestoreSqlPlan sql,
        object? nowValue,
        CancellationToken cancellationToken)
    {
        var parameters = Parameters(request, nowValue);
        if (sql.CandidateFenceSql is not null)
        {
            var fenced = sql.CandidateFenceReturnsRows
                ? await CountRowsAsync(transaction, sql.CandidateFenceSql, parameters, cancellationToken).ConfigureAwait(false)
                : await ExecuteNonQueryAsync(transaction, sql.CandidateFenceSql, parameters, cancellationToken).ConfigureAwait(false);
            if (fenced != request.ExpectedTableCount)
            {
                return MvActivationResult.Rejected(
                    MvActivationFailureReason.CandidateMissing,
                    "The complete serving materialized-view registry row set was not found.");
            }
        }

        var rows = await ReadCandidateRowsAsync(transaction, sql.CandidateLockSql, parameters, cancellationToken)
            .ConfigureAwait(false);
        if (AfterCandidateLockTestBarrier.Value is { } barrier)
        {
            await barrier(transaction, cancellationToken).ConfigureAwait(false);
        }

        var pointer = await ReadActivePointerAsync(transaction, sql.ActivePointerLockSql, parameters, cancellationToken)
            .ConfigureAwait(false);
        if (AfterActivePointerReadTestBarrier.Value is { } pointerReadBarrier)
        {
            await pointerReadBarrier(transaction, cancellationToken).ConfigureAwait(false);
        }

        var validation = ValidateLockedSnapshot(request, rows, pointer);
        if (validation is not null)
        {
            return validation;
        }

        var expectedRestoreCount = rows.Count(row => row.Status is MvStatus.CatchingUp or MvStatus.Ready);
        if (expectedRestoreCount == 0)
        {
            return MvActivationResult.ActiveStatusRestored(request.ExpectedActiveGeneration);
        }

        var restored = await ExecuteNonQueryAsync(transaction, sql.RestoreStatusesSql, parameters, cancellationToken)
            .ConfigureAwait(false);
        if (restored != expectedRestoreCount)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.ConcurrentSuperseded,
                "The serving materialized-view lifecycle rows changed before status restoration.");
        }

        return MvActivationResult.ActiveStatusRestored(request.ExpectedActiveGeneration);
    }

    private static async Task<IReadOnlyList<MvActiveStatusRestoreDbRow>> ReadCandidateRowsAsync(
        DbTransaction transaction,
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(transaction, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<MvActiveStatusRestoreDbRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(
                new MvActiveStatusRestoreDbRow(
                    RequiredString(reader, 0, "ServiceId"),
                    RequiredString(reader, 1, "ViewName"),
                    RequiredInt(reader, 2, "ViewVersion"),
                    RequiredString(reader, 3, "LogicalTable"),
                    RequiredString(reader, 4, "PhysicalTable"),
                    ParseStatus(RequiredString(reader, 5, "Status")),
                    NullableString(reader, 6),
                    NullableString(reader, 7),
                    NullableString(reader, 8),
                    NullableString(reader, 9),
                    NullableString(reader, 10),
                    NullableLong(reader, 11)));
        }

        return rows;
    }

    private static async Task<MvActiveStatusRestorePointer?> ReadActivePointerAsync(
        DbTransaction transaction,
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(transaction, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var pointer = new MvActiveStatusRestorePointer(
            RequiredInt(reader, 0, "ActiveVersion"),
            RequiredLong(reader, 1, "ActiveGeneration"));
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The materialized-view active pointer returned duplicate rows.");
        }

        return pointer;
    }

    private static MvActivationResult? ValidateLockedSnapshot(
        MvActiveStatusRestoreRequest request,
        IReadOnlyList<MvActiveStatusRestoreDbRow> rows,
        MvActiveStatusRestorePointer? pointer)
    {
        if (rows.Count != request.ExpectedTableCount)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.CandidateMissing,
                "The complete serving materialized-view registry row set was not found.");
        }

        var expectedRows = request.Rows.ToDictionary(row => row.LogicalTable, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!string.Equals(row.ServiceId, request.ServiceId, StringComparison.Ordinal) ||
                !string.Equals(row.ViewName, request.ViewName, StringComparison.Ordinal) ||
                row.ViewVersion != request.ViewVersion ||
                !seen.Add(row.LogicalTable) ||
                !expectedRows.TryGetValue(row.LogicalTable, out var expected) ||
                !string.Equals(row.PhysicalTable, expected.PhysicalTable, StringComparison.Ordinal))
            {
                return MvActivationResult.Rejected(
                    MvActivationFailureReason.IdentityMismatch,
                    "The locked registry row identity differs from the serving-status restore request.");
            }

            if (!expected.AllowedStatuses.Contains(row.Status))
            {
                return row.Status == MvStatus.Faulted
                    ? MvActivationResult.Rejected(
                        MvActivationFailureReason.Faulted,
                        "A serving materialized-view registry row is faulted; status restoration is refused.")
                    : MvActivationResult.Rejected(
                        MvActivationFailureReason.UnsafeLifecycle,
                        "A serving materialized-view registry row is outside the restore lifecycle contract.");
            }

            MvCheckpointTruth minimumCurrent;
            MvCheckpointTruth expectedTarget;
            MvCheckpointTruth actualCurrent;
            MvCheckpointTruth actualTarget;
            try
            {
                minimumCurrent = MvCheckpointTruthCodec.Decode(expected.MinimumCurrentCheckpointTruth);
                expectedTarget = MvCheckpointTruthCodec.Decode(expected.ExpectedTargetCheckpointTruth);
                actualCurrent = MvCheckpointTruthCodec.Decode(row.CurrentCheckpointTruth);
                actualTarget = MvCheckpointTruthCodec.Decode(row.TargetCheckpointTruth);
            }
            catch (MvCheckpointMalformedException ex)
            {
                return MvActivationResult.Rejected(MvActivationFailureReason.ProviderFailure, ex.Message);
            }

            if (!actualTarget.IsKnown || actualTarget.Provenance?.Kind != MvCheckpointProvenanceKind.AuthoritativeTargetCapture)
            {
                return MvActivationResult.Rejected(
                    MvActivationFailureReason.TargetUnknown,
                    "The locked serving materialized-view target checkpoint is not authoritative and Known.");
            }

            if (!string.Equals(
                    MvCheckpointTruthCodec.Encode(actualTarget),
                    MvCheckpointTruthCodec.Encode(expectedTarget),
                    StringComparison.Ordinal))
            {
                return MvActivationResult.Rejected(
                    MvActivationFailureReason.ConcurrentSuperseded,
                    "The locked serving materialized-view target checkpoint changed before restoration.");
            }

            if (!actualCurrent.IsKnown || actualCurrent.Position is null)
            {
                return MvActivationResult.Rejected(
                    MvActivationFailureReason.CurrentCheckpointUnknown,
                    "The locked serving materialized-view current checkpoint is Unknown.");
            }

            if (actualCurrent.Provenance is null || actualCurrent.Provenance.Kind == MvCheckpointProvenanceKind.LegacyCompatibility)
            {
                return MvActivationResult.Rejected(
                    MvActivationFailureReason.MissingProvenance,
                    "The locked serving materialized-view current checkpoint has legacy-only provenance.");
            }

            if (!minimumCurrent.IsKnown || minimumCurrent.Position is null)
            {
                return MvActivationResult.Rejected(
                    MvActivationFailureReason.CurrentCheckpointUnknown,
                    "The serving-status restore minimum current checkpoint is not Known.");
            }

            if (!actualCurrent.Position.IsLaterThanOrEqual(minimumCurrent.Position))
            {
                return MvActivationResult.Rejected(
                    MvActivationFailureReason.ConcurrentSuperseded,
                    "The locked serving materialized-view current checkpoint regressed.");
            }

            if (!actualCurrent.Satisfies(actualTarget))
            {
                return MvActivationResult.Rejected(
                    MvActivationFailureReason.BehindTarget,
                    "The locked serving materialized-view current checkpoint is behind its authoritative target.");
            }
        }

        if (pointer is null)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.ExpectedActiveConflict,
                "The serving materialized-view active pointer does not exist.");
        }

        if (pointer.ActiveVersion != request.ViewVersion)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.ExpectedActiveConflict,
                "The serving materialized-view active version changed before restoration.");
        }

        if (pointer.ActiveGeneration != request.ExpectedActiveGeneration)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.ExpectedGenerationConflict,
                "The serving materialized-view active generation changed before restoration.");
        }

        return null;
    }

    private static IReadOnlyDictionary<string, object?> Parameters(
        MvActiveStatusRestoreRequest request,
        object? nowValue) => new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["ServiceId"] = request.ServiceId,
        ["ViewName"] = request.ViewName,
        ["ViewVersion"] = request.ViewVersion,
        ["ExpectedActiveGeneration"] = request.ExpectedActiveGeneration,
        ["Now"] = nowValue
    };

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

    private static string RequiredString(DbDataReader reader, int ordinal, string fieldName) =>
        NullableString(reader, ordinal) ??
        throw new InvalidOperationException($"The serving materialized-view registry field '{fieldName}' is null.");

    private static string? NullableString(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => value.ToString()
        };
    }

    private static int RequiredInt(DbDataReader reader, int ordinal, string fieldName) =>
        Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static long RequiredLong(DbDataReader reader, int ordinal, string fieldName) =>
        Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static long? NullableLong(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static MvStatus ParseStatus(string value) =>
        Enum.TryParse<MvStatus>(value, true, out var status)
            ? status
            : throw new InvalidOperationException("The serving materialized-view registry contains an unknown lifecycle status.");

    private static DbTransaction RequireDbTransaction(System.Data.IDbTransaction transaction) =>
        transaction as DbTransaction ??
        throw new ArgumentException("The caller transaction must derive from DbTransaction.", nameof(transaction));

    private static MvActivationResult Retryable(int attempt) =>
        MvActivationResult.Rejected(
                MvActivationFailureReason.RetryableConcurrency,
                $"The provider reported transient concurrency during {SavepointOperationName}; retry from a fresh read boundary.")
            with { AttemptCount = attempt };

    private static MvActivationResult RetryExhausted(int attempts) =>
        MvActivationResult.Rejected(
                MvActivationFailureReason.RetryExhausted,
                $"The provider's transient concurrency retry bound was exhausted during {SavepointOperationName}.")
            with { AttemptCount = attempts };

    private static bool IsTransientConcurrency(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException dbException && IsTransientDbException(dbException))
            {
                return true;
            }

            if (current.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("serialization", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("database is locked", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("database is busy", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("lock timeout", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTransientDbException(DbException exception)
    {
        var sqlState = ReadProviderProperty(exception, "SqlState");
        if (sqlState is not null &&
            (sqlState.StartsWith("40", StringComparison.OrdinalIgnoreCase) ||
             sqlState.Equals("41000", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var propertyName in new[] { "Number", "SqliteErrorCode", "Code" })
        {
            if (!int.TryParse(ReadProviderProperty(exception, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            {
                continue;
            }

            if (code is 5 or 6 or 1205 or 1213 or 1222 or 3960 or 1204 or 41302 or 41305 or 41325 or 41839)
            {
                return true;
            }
        }

        return exception.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("serialization", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("busy", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("locked", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadProviderProperty(object value, string propertyName) =>
        value.GetType().GetProperty(propertyName)?.GetValue(value)?.ToString();

    private sealed record MvActiveStatusRestoreDbRow(
        string ServiceId,
        string ViewName,
        int ViewVersion,
        string LogicalTable,
        string PhysicalTable,
        MvStatus Status,
        string? CurrentPosition,
        string? TargetPosition,
        string? CurrentCheckpointTruth,
        string? TargetCheckpointTruth,
        string? LastSortableUniqueId,
        long? AppliedEventVersion);

    private sealed record MvActiveStatusRestorePointer(int ActiveVersion, long ActiveGeneration);

    private sealed class TestBarrierScope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
