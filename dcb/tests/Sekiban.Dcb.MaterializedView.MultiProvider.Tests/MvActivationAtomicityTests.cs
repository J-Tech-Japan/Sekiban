using System.Data.Common;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.MaterializedView;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

[CollectionDefinition(nameof(PostgresMvCollection))]
public sealed class PostgresMvCollection : ICollectionFixture<PostgresMvFixture>;

[Collection(nameof(PostgresMvCollection))]
public sealed class PostgresMvActivationAtomicityTests(PostgresMvFixture fixture) : MvActivationAtomicityTestsBase(fixture);

[Collection(nameof(MySqlMvCollection))]
public sealed class MySqlMvActivationAtomicityTests(MySqlMvFixture fixture) : MvActivationAtomicityTestsBase(fixture);

[Collection(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvActivationAtomicityTests(SqlServerMvFixture fixture) : MvActivationAtomicityTestsBase(fixture);

[Collection(nameof(SqliteMvCollection))]
public sealed class SqliteMvActivationAtomicityTests(SqliteMvFixture fixture) : MvActivationAtomicityTestsBase(fixture);

public abstract class MvActivationAtomicityTestsBase(MultiProviderFixtureBase fixture)
{
    [SkippableFact]
    public Task SameGenerationConcurrency_HasExactlyOneWinner() =>
        MvActivationAtomicityAssertions.AssertExactlyOneWinnerAsync(fixture);

    [SkippableFact]
    public Task CandidateChangeInterleaving_IsFencedAndRejectedWithoutPointerMutation() =>
        MvActivationAtomicityAssertions.AssertCandidateChangeInterleavingAsync(fixture);

    [SkippableFact]
    public Task InvalidAndDirectBypassRequests_LeavePointerUnchanged() =>
        MvActivationAtomicityAssertions.AssertInvalidAndBypassZeroMutationAsync(fixture);

    [SkippableFact]
    public Task ProviderFailure_LeavesCandidateRetryableAndRetrySucceeds() =>
        MvActivationAtomicityAssertions.AssertProviderFailureRetryAsync(fixture);

    [SkippableFact]
    public Task CallerTransactionFailure_IsRolledBackToOperationSavepoint() =>
        MvActivationAtomicityAssertions.AssertCallerTransactionRollbackAsync(fixture);

    [SkippableFact]
    public Task MarkActiveCountMismatch_RollsBackPointerAndEveryCandidateMutation() =>
        MvActivationAtomicityAssertions.AssertMarkCountMismatchRollbackAsync(fixture);
}

internal static class MvActivationAtomicityAssertions
{
    private const string ServiceId = "activation-service";
    private const string ViewName = "AtomicView";
    private const int ViewVersion = 2;

    public static async Task AssertExactlyOneWinnerAsync(MultiProviderFixtureBase fixture)
    {
        var (store, request) = await PrepareEligibleCandidateAsync(fixture).ConfigureAwait(false);

        var results = await Task.WhenAll(
            store.TryActivateAsync(request),
            store.TryActivateAsync(request)).ConfigureAwait(false);

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded && result.IsConflict);
        var active = await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false);
        Assert.NotNull(active);
        Assert.Equal(ViewVersion, active.ActiveVersion);
        Assert.Equal(1, active.Generation);
        Assert.All(
            await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false),
            entry => Assert.Equal(MvStatus.Active, entry.Status));
    }

    public static async Task AssertCandidateChangeInterleavingAsync(MultiProviderFixtureBase fixture)
    {
        var (store, request) = await PrepareEligibleCandidateAsync(fixture).ConfigureAwait(false);
        await using var blockerConnection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var blockerTransaction = await blockerConnection.BeginTransactionAsync().ConfigureAwait(false);
        var changedTarget = EncodeCheckpoint("changed-target", DateTime.UnixEpoch.AddMinutes(3),
            MvCheckpointProvenance.AuthoritativeTargetCapture());
        await blockerConnection.ExecuteAsync(
            new CommandDefinition(
                fixture.CandidateTruthUpdateSql,
                new { ServiceId, ViewName, ViewVersion, TargetTruth = changedTarget },
                blockerTransaction)).ConfigureAwait(false);

        // Microsoft.Data.Sqlite may synchronously wait for its busy timeout inside ExecuteAsync, so put the
        // competing operation on its own worker to make the lock interleaving observable for every provider.
        var activationTask = Task.Run(() => store.TryActivateAsync(request));
        await Task.Delay(100).ConfigureAwait(false);
        Assert.False(activationTask.IsCompleted, "Activation must wait for the transaction holding the candidate fence.");
        await blockerTransaction.CommitAsync().ConfigureAwait(false);

        var result = await activationTask.ConfigureAwait(false);
        Assert.False(result.Succeeded);
        Assert.Equal(MvActivationFailureReason.ConcurrentSuperseded, result.FailureReason);
        Assert.Null(await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false));
    }

    public static async Task AssertInvalidAndBypassZeroMutationAsync(MultiProviderFixtureBase fixture)
    {
        var (store, request) = await PrepareEligibleCandidateAsync(fixture).ConfigureAwait(false);
        var invalid = await store.TryActivateAsync(request with
        {
            ExpectedTargetCheckpointTruth = MvCheckpointTruthCodec.Encode(
                MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.ReadUnavailable))
        }).ConfigureAwait(false);
        Assert.False(invalid.Succeeded);
        Assert.Equal(MvActivationFailureReason.TargetUnknown, invalid.FailureReason);
        Assert.Null(await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false));

        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await connection.ExecuteAsync(
                """
                UPDATE sekiban_mv_registry
                SET status = 'catchingup'
                WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ViewVersion;
                """,
                new { ServiceId, ViewName, ViewVersion }).ConfigureAwait(false);
        }

        var bypass = await store.TryActivateAsync(request).ConfigureAwait(false);
        Assert.False(bypass.Succeeded);
        Assert.Equal(MvActivationFailureReason.ConcurrentSuperseded, bypass.FailureReason);
        Assert.Null(await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false));
    }

    public static async Task AssertProviderFailureRetryAsync(MultiProviderFixtureBase fixture)
    {
        var (store, request) = await PrepareEligibleCandidateAsync(fixture).ConfigureAwait(false);
        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await connection.ExecuteAsync("DROP TABLE sekiban_mv_active;").ConfigureAwait(false);
        }

        await Assert.ThrowsAnyAsync<Exception>(() => store.TryActivateAsync(request)).ConfigureAwait(false);
        Assert.All(
            await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false),
            entry => Assert.Equal(MvStatus.Ready, entry.Status));

        await store.EnsureInfrastructureAsync().ConfigureAwait(false);
        var retry = await store.TryActivateAsync(request).ConfigureAwait(false);
        Assert.True(retry.Succeeded);
        Assert.Equal(1, retry.NewGeneration);
    }

    public static async Task AssertCallerTransactionRollbackAsync(MultiProviderFixtureBase fixture)
    {
        var (store, request) = await PrepareEligibleCandidateAsync(fixture).ConfigureAwait(false);
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        var rejected = await store.TryActivateAsync(request with { CandidateCount = request.CandidateCount + 1 }, transaction)
            .ConfigureAwait(false);
        Assert.False(rejected.Succeeded);
        await transaction.CommitAsync().ConfigureAwait(false);

        Assert.Null(await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false));
        Assert.All(
            await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false),
            entry => Assert.Equal(MvStatus.Ready, entry.Status));
    }

    public static async Task AssertMarkCountMismatchRollbackAsync(MultiProviderFixtureBase fixture)
    {
        var (store, request) = await PrepareEligibleCandidateAsync(fixture).ConfigureAwait(false);
        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await connection.ExecuteAsync(fixture.CreateCandidateMismatchTriggerSql).ConfigureAwait(false);
        }

        var mismatch = await store.TryActivateAsync(request).ConfigureAwait(false);
        Assert.False(mismatch.Succeeded);
        Assert.Equal(MvActivationFailureReason.ConcurrentSuperseded, mismatch.FailureReason);
        Assert.Null(await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false));
        Assert.All(
            await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false),
            entry => Assert.Equal(MvStatus.Ready, entry.Status));

        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await connection.ExecuteAsync(fixture.DropCandidateMismatchTriggerSql).ConfigureAwait(false);
        }

        var retry = await store.TryActivateAsync(request).ConfigureAwait(false);
        Assert.True(retry.Succeeded);
    }

    private static async Task<(IMvRegistryStore Store, MvActivationRequest Request)> PrepareEligibleCandidateAsync(
        MultiProviderFixtureBase fixture)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Integration fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var store = fixture.Services.GetRequiredService<IMvRegistryStore>();
        await store.EnsureInfrastructureAsync().ConfigureAwait(false);
        var current = Checkpoint("same", DateTime.UnixEpoch.AddMinutes(2),
            MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp));
        var target = Checkpoint("same", DateTime.UnixEpoch.AddMinutes(2),
            MvCheckpointProvenance.AuthoritativeTargetCapture());
        await store.RegisterAsync(CreateEntry("orders", current, target)).ConfigureAwait(false);
        await store.RegisterAsync(CreateEntry("items", current, target)).ConfigureAwait(false);
        var entries = await store.GetEntriesAsync(ServiceId, ViewName, ViewVersion).ConfigureAwait(false);
        var (eligibility, request) = MvActivationEligibility.Evaluate(ServiceId, ViewName, ViewVersion, entries, null);
        Assert.True(eligibility.IsEligible);
        return (store, Assert.IsType<MvActivationRequest>(request));
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

    private static MvCheckpointTruth Checkpoint(
        string seed,
        DateTime timestamp,
        MvCheckpointProvenance provenance) =>
        MvCheckpointTruth.Known(
            new SortableUniqueId(SortableUniqueId.Generate(timestamp, StableGuid(seed))),
            provenance);

    private static string EncodeCheckpoint(
        string seed,
        DateTime timestamp,
        MvCheckpointProvenance provenance) =>
        MvCheckpointTruthCodec.Encode(Checkpoint(seed, timestamp, provenance));

    private static Guid StableGuid(string seed) => seed switch
    {
        "same" => new Guid("00000000-0000-0000-0000-000000000010"),
        "changed-target" => new Guid("00000000-0000-0000-0000-000000000020"),
        _ => throw new ArgumentOutOfRangeException(nameof(seed))
    };
}
