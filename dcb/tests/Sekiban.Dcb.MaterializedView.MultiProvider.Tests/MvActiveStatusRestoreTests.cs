using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.MaterializedView;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

[Collection(nameof(PostgresMvCollection))]
public sealed class PostgresMvActiveStatusRestoreTests(PostgresMvFixture fixture) : MvActiveStatusRestoreTestsBase(fixture);

[Collection(nameof(MySqlMvCollection))]
public sealed class MySqlMvActiveStatusRestoreTests(MySqlMvFixture fixture) : MvActiveStatusRestoreTestsBase(fixture);

[Collection(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvActiveStatusRestoreTests(SqlServerMvFixture fixture) : MvActiveStatusRestoreTestsBase(fixture);

[Collection(nameof(SqliteMvCollection))]
public sealed class SqliteMvActiveStatusRestoreTests(SqliteMvFixture fixture) : MvActiveStatusRestoreTestsBase(fixture);

public abstract class MvActiveStatusRestoreTestsBase(MultiProviderFixtureBase fixture)
{
    [SkippableFact]
    public Task RestoresMixedLegacyStatuses_WithoutChangingPointerOrProgress() =>
        MvActiveStatusRestoreAssertions.AssertRestoresMixedLegacyStatusesAsync(fixture);

    [SkippableFact]
    public Task GenerationMismatch_RejectsWithoutChangingLegacyStatuses() =>
        MvActiveStatusRestoreAssertions.AssertGenerationMismatchAsync(fixture);

    [SkippableFact]
    public Task TargetRecapture_RejectsWithoutChangingLegacyStatuses() =>
        MvActiveStatusRestoreAssertions.AssertTargetRecaptureAsync(fixture);

    [SkippableFact]
    public Task UnknownCurrentTruth_RejectsWithoutChangingLegacyStatuses() =>
        MvActiveStatusRestoreAssertions.AssertUnknownCurrentTruthAsync(fixture);

    [SkippableFact]
    public Task ActiveVersion_StatusUpdateCannotDowngradeItToCatchUp() =>
        MvActiveStatusRestoreAssertions.AssertActiveStatusGuardAsync(fixture);

    [SkippableFact]
    public Task SupersessionBetweenRegistryAndPointerLocks_IsRejectedWithoutAStaleRestore() =>
        MvActiveStatusRestoreAssertions.AssertSupersessionBetweenLocksAsync(fixture);

    [SkippableFact]
    public Task MissingRegistryRow_IsRejectedWithoutPointerMutation() =>
        MvActiveStatusRestoreAssertions.AssertMissingRowAsync(fixture);

    [SkippableFact]
    public Task TransientConcurrency_UsesFreshBoundedRetries_ThenExposesExhaustion() =>
        MvActiveStatusRestoreAssertions.AssertRetryExhaustionAsync(fixture);

    [SkippableFact]
    public Task CallerTransaction_RejectedRestoreRollsBackToSavepoint() =>
        MvActiveStatusRestoreAssertions.AssertCallerTransactionRollbackAsync(fixture);

    [SkippableFact]
    public Task PersistenceFailure_RollsBackStatusRestoration() =>
        MvActiveStatusRestoreAssertions.AssertPersistenceFailureRollbackAsync(fixture);

    [SkippableFact]
    public Task Cancellation_RollsBackStatusRestoration() =>
        MvActiveStatusRestoreAssertions.AssertCancellationRollbackAsync(fixture);
}

internal static class MvActiveStatusRestoreAssertions
{
    private const string ServiceId = "activation-service";
    private const string ViewName = "AtomicView";
    private const int ViewVersion = 2;

    public static async Task AssertRestoresMixedLegacyStatusesAsync(MultiProviderFixtureBase fixture)
    {
        var (store, active, entries) = await PrepareServingVersionAsync(fixture).ConfigureAwait(false);
        await SetStatusAsync(fixture, "orders", "catchingup").ConfigureAwait(false);
        var damaged = await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false);
        var request = MvActiveStatusRestoreRequest.FromEntries(
            ServiceId,
            ViewName,
            ViewVersion,
            active.Generation,
            damaged);

        var result = await store.TryRestoreActiveStatusAsync(request).ConfigureAwait(false);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(MvActivationFailureReason.None, result.FailureReason);
        Assert.Equal(active.Generation, result.NewGeneration);
        var restored = await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false);
        Assert.Equal(2, restored.Count);
        Assert.All(restored, entry => Assert.Equal(MvStatus.Active, entry.Status));
        var restoredActive = await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false);
        Assert.NotNull(restoredActive);
        Assert.Equal(active.ActiveVersion, restoredActive.ActiveVersion);
        Assert.Equal(active.Generation, restoredActive.Generation);
        var expectedByLogical = entries.ToDictionary(entry => entry.LogicalTable, StringComparer.Ordinal);
        foreach (var actual in restored)
        {
            var expected = expectedByLogical[actual.LogicalTable];
            Assert.Equal(expected.PhysicalTable, actual.PhysicalTable);
            Assert.Equal(expected.CurrentPosition, actual.CurrentPosition);
            Assert.Equal(expected.TargetPosition, actual.TargetPosition);
            Assert.Equal(
                MvCheckpointTruthCodec.Encode(expected.CurrentCheckpointTruth),
                MvCheckpointTruthCodec.Encode(actual.CurrentCheckpointTruth));
            Assert.Equal(
                MvCheckpointTruthCodec.Encode(expected.TargetCheckpointTruth),
                MvCheckpointTruthCodec.Encode(actual.TargetCheckpointTruth));
            Assert.Equal(expected.LastSortableUniqueId, actual.LastSortableUniqueId);
            Assert.Equal(expected.AppliedEventVersion, actual.AppliedEventVersion);
        }
    }

    public static async Task AssertGenerationMismatchAsync(MultiProviderFixtureBase fixture)
    {
        var (store, active, _) = await PrepareServingVersionAsync(fixture).ConfigureAwait(false);
        await SetStatusAsync(fixture, "orders", "catchingup").ConfigureAwait(false);
        var damaged = await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false);
        var request = MvActiveStatusRestoreRequest.FromEntries(
            ServiceId,
            ViewName,
            ViewVersion,
            active.Generation - 1,
            damaged);

        var result = await store.TryRestoreActiveStatusAsync(request).ConfigureAwait(false);

        Assert.False(result.Succeeded);
        Assert.Equal(MvActivationFailureReason.ExpectedGenerationConflict, result.FailureReason);
        Assert.Equal(MvStatus.CatchingUp, (await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false))
            .Single(entry => entry.LogicalTable == "orders").Status);
        var unchanged = await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false);
        Assert.NotNull(unchanged);
        Assert.Equal(active.Generation, unchanged.Generation);
    }

    public static async Task AssertTargetRecaptureAsync(MultiProviderFixtureBase fixture)
    {
        var (store, active, _) = await PrepareServingVersionAsync(fixture).ConfigureAwait(false);
        await SetStatusAsync(fixture, "orders", "ready").ConfigureAwait(false);
        var damaged = await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false);
        var request = MvActiveStatusRestoreRequest.FromEntries(
            ServiceId,
            ViewName,
            ViewVersion,
            active.Generation,
            damaged);
        var recapturedTarget = MvCheckpointTruthCodec.Encode(
            MvCheckpointTruth.Known(
                new SortableUniqueId(SortableUniqueId.Generate(
                    DateTime.UnixEpoch.AddMinutes(4),
                    new Guid("00000000-0000-0000-0000-000000000030"))),
                MvCheckpointProvenance.AuthoritativeTargetCapture()));
        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await connection.ExecuteAsync(
                fixture.CandidateTruthUpdateSql,
                new { ServiceId, ViewName, ViewVersion, TargetTruth = recapturedTarget }).ConfigureAwait(false);
        }

        var result = await store.TryRestoreActiveStatusAsync(request).ConfigureAwait(false);

        Assert.False(result.Succeeded);
        Assert.Equal(MvActivationFailureReason.ConcurrentSuperseded, result.FailureReason);
        Assert.Equal(
            MvStatus.Ready,
            (await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false))
                .Single(entry => entry.LogicalTable == "orders").Status);
        var unchanged = await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false);
        Assert.NotNull(unchanged);
        Assert.Equal(active.Generation, unchanged.Generation);
    }

    public static async Task AssertUnknownCurrentTruthAsync(MultiProviderFixtureBase fixture)
    {
        var (store, active, _) = await PrepareServingVersionAsync(fixture).ConfigureAwait(false);
        await SetStatusAsync(fixture, "orders", "catchingup").ConfigureAwait(false);
        var request = MvActiveStatusRestoreRequest.FromEntries(
            ServiceId,
            ViewName,
            ViewVersion,
            active.Generation,
            await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false));
        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await connection.ExecuteAsync(
                """
                UPDATE sekiban_mv_registry
                SET current_checkpoint_truth = NULL
                WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ViewVersion
                  AND logical_table = 'orders';
                """,
                new { ServiceId, ViewName, ViewVersion }).ConfigureAwait(false);
        }

        var result = await store.TryRestoreActiveStatusAsync(request).ConfigureAwait(false);

        Assert.False(result.Succeeded);
        Assert.Equal(MvActivationFailureReason.CurrentCheckpointUnknown, result.FailureReason);
        Assert.Equal(
            MvStatus.CatchingUp,
            (await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false))
                .Single(entry => entry.LogicalTable == "orders").Status);
        var unchanged = await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false);
        Assert.NotNull(unchanged);
        Assert.Equal(active.Generation, unchanged.Generation);
    }

    public static async Task AssertActiveStatusGuardAsync(MultiProviderFixtureBase fixture)
    {
        var (store, _, _) = await PrepareServingVersionAsync(fixture).ConfigureAwait(false);

        await store.UpdateStatusAsync(
                ServiceId,
                ViewName,
                ViewVersion,
                MvStatus.CatchingUp)
            .ConfigureAwait(false);

        Assert.All(
            await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false),
            entry => Assert.Equal(MvStatus.Active, entry.Status));
    }

    public static async Task AssertSupersessionBetweenLocksAsync(MultiProviderFixtureBase fixture)
    {
        var (store, active, _) = await PrepareServingVersionAsync(fixture).ConfigureAwait(false);
        await SetStatusAsync(fixture, "orders", "catchingup").ConfigureAwait(false);
        var damaged = await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false);
        var request = MvActiveStatusRestoreRequest.FromEntries(
            ServiceId,
            ViewName,
            ViewVersion,
            active.Generation,
            damaged);
        var candidateLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueRestore = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var isSqlite = fixture.DatabaseTypeForTests == MvDbType.Sqlite;

        using var barrier = MvActiveStatusRestoreExecution.PushAfterCandidateLockTestBarrier(
            async (transaction, cancellationToken) =>
            {
                candidateLocked.TrySetResult();
                await continueRestore.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (isSqlite)
                {
                    await transaction.Connection!.ExecuteAsync(
                            new CommandDefinition(
                                "UPDATE sekiban_mv_active SET active_generation = active_generation + 1 WHERE service_id = @ServiceId AND view_name = @ViewName;",
                                new { ServiceId, ViewName },
                                transaction,
                                cancellationToken: cancellationToken))
                        .ConfigureAwait(false);
                }
            });

        var restoreTask = store.TryRestoreActiveStatusAsync(request);
        await candidateLocked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        try
        {
            if (!isSqlite)
            {
                await using var superseder = await fixture.OpenConnectionAsync().ConfigureAwait(false);
                await superseder.ExecuteAsync(
                        "UPDATE sekiban_mv_active SET active_generation = active_generation + 1 WHERE service_id = @ServiceId AND view_name = @ViewName;",
                        new { ServiceId, ViewName })
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            continueRestore.TrySetResult();
        }

        var result = await restoreTask.ConfigureAwait(false);
        Assert.False(result.Succeeded);
        Assert.Equal(MvActivationFailureReason.ExpectedGenerationConflict, result.FailureReason);
        Assert.Equal(
            MvStatus.CatchingUp,
            (await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false))
                .Single(entry => entry.LogicalTable == "orders")
                .Status);
        var pointer = await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false);
        Assert.NotNull(pointer);
        Assert.Equal(active.Generation + (isSqlite ? 0 : 1), pointer.Generation);
    }

    public static async Task AssertMissingRowAsync(MultiProviderFixtureBase fixture)
    {
        var (store, active, entries) = await PrepareServingVersionAsync(fixture).ConfigureAwait(false);
        await SetStatusAsync(fixture, "orders", "catchingup").ConfigureAwait(false);
        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await connection.ExecuteAsync(
                    "DELETE FROM sekiban_mv_registry WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ViewVersion AND logical_table = 'items';",
                    new { ServiceId, ViewName, ViewVersion })
                .ConfigureAwait(false);
        }

        var request = MvActiveStatusRestoreRequest.FromEntries(
            ServiceId,
            ViewName,
            ViewVersion,
            active.Generation,
            entries);
        var result = await store.TryRestoreActiveStatusAsync(request).ConfigureAwait(false);

        Assert.False(result.Succeeded);
        Assert.Equal(MvActivationFailureReason.CandidateMissing, result.FailureReason);
        Assert.Equal(
            MvStatus.CatchingUp,
            (await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false))
                .Single(entry => entry.LogicalTable == "orders")
                .Status);
        var pointer = await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false);
        Assert.NotNull(pointer);
        Assert.Equal(active.Generation, pointer.Generation);
    }

    public static async Task AssertRetryExhaustionAsync(MultiProviderFixtureBase fixture)
    {
        var (store, active, _) = await PrepareServingVersionAsync(fixture).ConfigureAwait(false);
        await SetStatusAsync(fixture, "orders", "catchingup").ConfigureAwait(false);
        var request = MvActiveStatusRestoreRequest.FromEntries(
            ServiceId,
            ViewName,
            ViewVersion,
            active.Generation,
            await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false)) with
        {
            MaxAttempts = 2
        };

        using var barrier = MvActiveStatusRestoreExecution.PushAfterCandidateLockTestBarrier(
            (_, _) => Task.FromException(new InvalidOperationException("deadlock simulated for bounded retry characterization")));
        var result = await store.TryRestoreActiveStatusAsync(request).ConfigureAwait(false);

        Assert.False(result.Succeeded);
        Assert.Equal(MvActivationFailureReason.RetryExhausted, result.FailureReason);
        Assert.True(result.IsRetryableConcurrency);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(
            MvStatus.CatchingUp,
            (await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false))
                .Single(entry => entry.LogicalTable == "orders")
                .Status);
        var pointer = await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false);
        Assert.NotNull(pointer);
        Assert.Equal(active.Generation, pointer.Generation);
    }

    public static async Task AssertCallerTransactionRollbackAsync(MultiProviderFixtureBase fixture)
    {
        var (store, active, _) = await PrepareServingVersionAsync(fixture).ConfigureAwait(false);
        await SetStatusAsync(fixture, "orders", "catchingup").ConfigureAwait(false);
        var damaged = await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false);
        var request = MvActiveStatusRestoreRequest.FromEntries(
            ServiceId,
            ViewName,
            ViewVersion,
            active.Generation - 1,
            damaged);

        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        await using (var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false))
        {
            var result = await store.TryRestoreActiveStatusAsync(request, transaction).ConfigureAwait(false);
            Assert.False(result.Succeeded);
            Assert.Equal(MvActivationFailureReason.ExpectedGenerationConflict, result.FailureReason);
            await transaction.CommitAsync().ConfigureAwait(false);
        }

        Assert.Equal(
            MvStatus.CatchingUp,
            (await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false))
                .Single(entry => entry.LogicalTable == "orders")
                .Status);
    }

    public static async Task AssertPersistenceFailureRollbackAsync(MultiProviderFixtureBase fixture)
    {
        var (store, active, _) = await PrepareServingVersionAsync(fixture).ConfigureAwait(false);
        await SetStatusAsync(fixture, "orders", "catchingup").ConfigureAwait(false);
        var request = MvActiveStatusRestoreRequest.FromEntries(
            ServiceId,
            ViewName,
            ViewVersion,
            active.Generation,
            await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false));

        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await connection.ExecuteAsync("DROP TABLE sekiban_mv_active;").ConfigureAwait(false);
        }

        await Assert.ThrowsAnyAsync<Exception>(() => store.TryRestoreActiveStatusAsync(request)).ConfigureAwait(false);
        Assert.Equal(
            MvStatus.CatchingUp,
            (await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false))
                .Single(entry => entry.LogicalTable == "orders")
                .Status);
    }

    public static async Task AssertCancellationRollbackAsync(MultiProviderFixtureBase fixture)
    {
        var (store, active, _) = await PrepareServingVersionAsync(fixture).ConfigureAwait(false);
        await SetStatusAsync(fixture, "orders", "catchingup").ConfigureAwait(false);
        var request = MvActiveStatusRestoreRequest.FromEntries(
            ServiceId,
            ViewName,
            ViewVersion,
            active.Generation,
            await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false));
        using var cancellation = new CancellationTokenSource();
        using var barrier = MvActiveStatusRestoreExecution.PushAfterCandidateLockTestBarrier(
            (_, _) =>
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => store.TryRestoreActiveStatusAsync(request, cancellationToken: cancellation.Token))
            .ConfigureAwait(false);
        Assert.Equal(
            MvStatus.CatchingUp,
            (await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false))
                .Single(entry => entry.LogicalTable == "orders")
                .Status);
    }

    private static async Task<(IMvRegistryStore Store, MvActiveEntry Active, IReadOnlyList<MvRegistryEntry> Entries)> PrepareServingVersionAsync(
        MultiProviderFixtureBase fixture)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Integration fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var store = fixture.Services.GetRequiredService<IMvRegistryStore>();
        await store.EnsureInfrastructureAsync().ConfigureAwait(false);
        var current = MvCheckpointTruth.Known(
            new SortableUniqueId(SortableUniqueId.Generate(
                DateTime.UnixEpoch.AddMinutes(2),
                new Guid("00000000-0000-0000-0000-000000000010"))),
            MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp));
        var target = MvCheckpointTruth.Known(
            new SortableUniqueId(SortableUniqueId.Generate(
                DateTime.UnixEpoch.AddMinutes(2),
                new Guid("00000000-0000-0000-0000-000000000010"))),
            MvCheckpointProvenance.AuthoritativeTargetCapture());
        await store.RegisterAsync(CreateEntry("orders", current, target)).ConfigureAwait(false);
        await store.RegisterAsync(CreateEntry("items", current, target)).ConfigureAwait(false);
        var entries = await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false);
        var (eligibility, request) = MvActivationEligibility.Evaluate(ServiceId, ViewName, ViewVersion, entries, null);
        Assert.True(eligibility.IsEligible);
        var activation = await store.TryActivateAsync(Assert.IsType<MvActivationRequest>(request)).ConfigureAwait(false);
        Assert.True(activation.Succeeded, activation.Message);
        var active = await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false);
        Assert.NotNull(active);
        return (store, active, entries);
    }

    private static async Task SetStatusAsync(MultiProviderFixtureBase fixture, string logicalTable, string status)
    {
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        await connection.ExecuteAsync(
            """
            UPDATE sekiban_mv_registry
            SET status = @Status
            WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ViewVersion
              AND logical_table = @LogicalTable;
            """,
            new { ServiceId, ViewName, ViewVersion, LogicalTable = logicalTable, Status = status }).ConfigureAwait(false);
    }

    private static MvRegistryEntry CreateEntry(
        string logicalTable,
        MvCheckpointTruth current,
        MvCheckpointTruth target) => new()
    {
        ServiceId = ServiceId,
        ViewName = ViewName,
        ViewVersion = ViewVersion,
        LogicalTable = logicalTable,
        PhysicalTable = $"atomic_{logicalTable}",
        Status = MvStatus.Ready,
        CurrentPosition = current.PositionValue,
        TargetPosition = target.PositionValue,
        CurrentCheckpointTruth = current,
        TargetCheckpointTruth = target,
        AppliedEventVersion = 1,
        LastUpdated = DateTimeOffset.UtcNow
    };
}
