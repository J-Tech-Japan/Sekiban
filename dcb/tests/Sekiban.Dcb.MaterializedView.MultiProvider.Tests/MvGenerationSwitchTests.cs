using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.ServiceId;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

[Collection(nameof(PostgresMvCollection))]
public sealed class PostgresMvGenerationSwitchTests(PostgresMvFixture fixture) : MvGenerationSwitchTestsBase(fixture);

[Collection(nameof(MySqlMvCollection))]
public sealed class MySqlMvGenerationSwitchTests(MySqlMvFixture fixture) : MvGenerationSwitchTestsBase(fixture);

[Collection(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvGenerationSwitchTests(SqlServerMvFixture fixture) : MvGenerationSwitchTestsBase(fixture);

[Collection(nameof(SqliteMvCollection))]
public sealed class SqliteMvGenerationSwitchTests(SqliteMvFixture fixture) : MvGenerationSwitchTestsBase(fixture);

public abstract class MvGenerationSwitchTestsBase(MultiProviderFixtureBase fixture)
{
    [SkippableFact]
    public Task PreparingNextGeneration_PreservesActiveGenerationAndIndependentProgress() =>
        MvGenerationSwitchAssertions.AssertIndependentPreparationAsync(fixture);

    [SkippableFact]
    public Task OrdinaryForwardAndReverse_UseEligibilityAndPersistDirection() =>
        MvGenerationSwitchAssertions.AssertOrdinaryForwardAndReverseAsync(fixture);

    [SkippableFact]
    public Task ForcedReverse_WaivesOnlyTruth_AndPublishesDurableAuditMetadata() =>
        MvGenerationSwitchAssertions.AssertForcedReverseAsync(fixture);

    [SkippableFact]
    public Task ForcedReverse_RejectsMissingIdentityAndStaleFenceWithoutMutation() =>
        MvGenerationSwitchAssertions.AssertForcedReverseFencesAsync(fixture);

    [SkippableFact]
    public Task ForcedReverse_RejectsStaleActiveCandidateWithoutMutation() =>
        MvGenerationSwitchAssertions.AssertStaleActiveCandidateRejectedAsync(fixture);

    [SkippableFact]
    public Task ForcedReverse_ProviderBoundaryRejectsActiveLifecycleWithoutMutation() =>
        MvGenerationSwitchAssertions.AssertProviderBoundaryRejectsActiveLifecycleAsync(fixture);

    [SkippableFact]
    public Task ForcedReverse_ProviderBoundaryFencesExactServiceViewAndVersion() =>
        MvGenerationSwitchAssertions.AssertProviderBoundaryExactIdentityAsync(fixture);

    [SkippableFact]
    public Task ConcurrentForcedReverse_ProviderFenceAllowsExactlyOneWinner() =>
        MvGenerationSwitchAssertions.AssertForcedReverseRaceAsync(fixture);

    [SkippableFact]
    public Task ForcedReverse_CallerTransactionRollback_IsRetryable() =>
        MvGenerationSwitchAssertions.AssertForcedReverseCallerRollbackAsync(fixture);
}

internal static class MvGenerationSwitchAssertions
{
    private const string ServiceId = MultiProviderFixtureBase.ServiceId;
    private const string ViewName = "GenerationView";

    public static async Task AssertIndependentPreparationAsync(MultiProviderFixtureBase fixture)
    {
        var (store, coordinator) = await PrepareAsync(fixture).ConfigureAwait(false);
        var first = new Host(1);
        await coordinator.PrepareGenerationAsync(first, ServiceId).ConfigureAwait(false);
        await fixture.Executor.CatchUpOnceAsync(first, ServiceId).ConfigureAwait(false);
        if (await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false) is null)
        {
            var initial = await coordinator.SwitchAsync(first, ServiceId).ConfigureAwait(false);
            Assert.True(initial.Succeeded, initial.Message);
        }
        var servingBefore = await store.GetEntriesAsync(ServiceId, ViewName, 1).ConfigureAwait(false);

        await coordinator.PrepareGenerationAsync(new Host(2), ServiceId).ConfigureAwait(false);

        var active = Assert.IsType<MvActiveEntry>(await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false));
        var servingAfter = await store.GetEntriesAsync(ServiceId, ViewName, 1).ConfigureAwait(false);
        var candidate = await store.GetEntriesAsync(ServiceId, ViewName, 2).ConfigureAwait(false);
        Assert.Equal(1, active.ActiveVersion);
        Assert.Equal(2, servingAfter.Count);
        Assert.Equal(2, candidate.Count);
        Assert.All(servingAfter, entry => Assert.Equal(MvStatus.Active, entry.Status));
        Assert.Equal(
            servingBefore.Select(entry => entry.CurrentCheckpointTruth),
            servingAfter.Select(entry => entry.CurrentCheckpointTruth));
        Assert.All(candidate, entry => Assert.True(entry.CurrentCheckpointTruth.IsUnknown));
        Assert.All(candidate, entry => Assert.True(entry.TargetCheckpointTruth.IsKnown));
        Assert.Empty(servingAfter.Select(entry => entry.PhysicalTable).Intersect(
            candidate.Select(entry => entry.PhysicalTable), StringComparer.Ordinal));
    }

    public static async Task AssertOrdinaryForwardAndReverseAsync(MultiProviderFixtureBase fixture)
    {
        var (store, coordinator) = await PrepareAsync(fixture).ConfigureAwait(false);
        await RegisterAsync(store, 1, known: true).ConfigureAwait(false);
        await RegisterAsync(store, 2, known: true).ConfigureAwait(false);

        var initial = await coordinator.SwitchAsync(new Host(1), ServiceId).ConfigureAwait(false);
        var forward = await coordinator.SwitchAsync(new Host(2), ServiceId).ConfigureAwait(false);
        var reverse = await coordinator.SwitchAsync(new Host(1), ServiceId).ConfigureAwait(false);

        Assert.True(initial.Succeeded);
        Assert.True(forward.Succeeded);
        Assert.True(reverse.Succeeded);
        var active = Assert.IsType<MvActiveEntry>(await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false));
        Assert.Equal(1, active.ActiveVersion);
        Assert.Equal(3, active.Generation);
        Assert.Equal(MvSwitchKind.Reverse, active.SwitchKind);
        Assert.Null(active.SwitchReason);
        Assert.NotNull(active.SwitchedAtUtc);
        Assert.All(await store.GetEntriesAsync(ServiceId, ViewName, 2).ConfigureAwait(false),
            entry => Assert.Equal(MvStatus.Ready, entry.Status));
    }

    public static async Task AssertForcedReverseAsync(MultiProviderFixtureBase fixture)
    {
        var (store, _) = await PrepareAsync(fixture).ConfigureAwait(false);
        await RegisterAsync(store, 1, known: false).ConfigureAwait(false);
        await RegisterAsync(store, 2, known: true).ConfigureAwait(false);
        await store.SetActiveAsync(ServiceId, ViewName, 2).ConfigureAwait(false);

#pragma warning disable CS0618
        var sourceStatus = new InMemoryMultiProjectionStateStore(new FixedServiceIdProvider(ServiceId));
#pragma warning restore CS0618
        var statusOptions = new ProjectionStatusOptions
        {
            Enabled = true,
            SamplingWindow = TimeSpan.Zero,
            HeartbeatInterval = TimeSpan.FromMinutes(1),
            FreshnessWindow = TimeSpan.FromMinutes(2)
        };
        var publisher = new MvProjectionStatusPublisher(
            sourceStatus,
            statusOptions,
            NullLogger<MvProjectionStatusPublisher>.Instance);
        var coordinator = CreateCoordinator(fixture, store, publisher);

        var ordinary = await coordinator.SwitchAsync(new Host(1), ServiceId).ConfigureAwait(false);
        Assert.False(ordinary.Succeeded);
        Assert.Equal(MvActivationFailureReason.CurrentCheckpointUnknown, ordinary.FailureReason);
        Assert.Equal(2, (await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false))!.ActiveVersion);

        const string reason = "operator rollback after incompatible downstream projection";
        var forced = await coordinator.ForceReverseAsync(new Host(1), 2, 1, reason, ServiceId).ConfigureAwait(false);
        Assert.True(forced.Succeeded);
        var active = Assert.IsType<MvActiveEntry>(await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false));
        Assert.Equal(1, active.ActiveVersion);
        Assert.Equal(2, active.Generation);
        Assert.Equal(MvSwitchKind.Forced, active.SwitchKind);
        Assert.Equal(reason, active.SwitchReason);
        Assert.NotNull(active.SwitchedAtUtc);

        var restartedCoordinator = CreateCoordinator(fixture, store);
        var restartedActive = Assert.IsType<MvActiveEntry>(
            await restartedCoordinator.GetActiveAsync(ViewName, ServiceId).ConfigureAwait(false));
        Assert.Equal(active, restartedActive);

        var identity = MvProjectionStatusIdentity.Create(ViewName, 1);
        var reader = new ProjectionStatusReader(
            sourceStatus,
            fixture.EventStore,
            new FixedServiceIdProvider(ServiceId),
            statusOptions);
        var typed = await reader.ReadAsync(new ProjectionStatusReadRequest(
            ServiceId,
            identity.ProjectorName,
            identity.ProjectorVersion)).ConfigureAwait(false);
        var observation = Assert.Single(typed.GetValue());
        Assert.Equal("forced", observation.SwitchKind);
        Assert.Equal(reason, observation.SwitchReason);
        Assert.Equal(active.SwitchedAtUtc, observation.SwitchedAtUtc);

        var serialized = new SerializedProjectionStatusReader(reader, new FixedServiceIdProvider(ServiceId));
        var bytes = await serialized.AcceptAsync(SerializedProjectionStatusReader.SerializeRequest(
            new ProjectionStatusReadRequest(ServiceId, identity.ProjectorName, identity.ProjectorVersion))).ConfigureAwait(false);
        var envelope = SerializedProjectionStatusReader.Deserialize(bytes.GetValue()).GetValue();
        var serializedObservation = Assert.Single(envelope.Snapshots!);
        Assert.Equal("forced", serializedObservation.SwitchKind);
        Assert.Equal(reason, serializedObservation.SwitchReason);
    }

    public static async Task AssertForcedReverseFencesAsync(MultiProviderFixtureBase fixture)
    {
        var (store, coordinator) = await PrepareAsync(fixture).ConfigureAwait(false);
        await RegisterAsync(store, 1, known: false).ConfigureAwait(false);
        await RegisterAsync(store, 2, known: true).ConfigureAwait(false);
        await store.SetActiveAsync(ServiceId, ViewName, 2).ConfigureAwait(false);

        var missing = await coordinator.ForceReverseAsync(new Host(0), 2, 1, "missing generation", ServiceId).ConfigureAwait(false);
        Assert.False(missing.Succeeded);
        Assert.Equal(MvActivationFailureReason.CandidateMissing, missing.FailureReason);

        var stale = await coordinator.ForceReverseAsync(new Host(1), 2, 0, "stale fence", ServiceId).ConfigureAwait(false);
        Assert.False(stale.Succeeded);
        Assert.Equal(MvActivationFailureReason.ExpectedActiveConflict, stale.FailureReason);

        var wrongDirection = await coordinator.ForceReverseAsync(new Host(3), 2, 1, "forward forbidden", ServiceId).ConfigureAwait(false);
        Assert.False(wrongDirection.Succeeded);
        Assert.Equal(MvActivationFailureReason.IdentityMismatch, wrongDirection.FailureReason);

        var active = Assert.IsType<MvActiveEntry>(await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false));
        Assert.Equal(2, active.ActiveVersion);
        Assert.Equal(1, active.Generation);
    }

    public static async Task AssertStaleActiveCandidateRejectedAsync(MultiProviderFixtureBase fixture)
    {
        var (store, coordinator) = await PrepareAsync(fixture).ConfigureAwait(false);
        await RegisterAsync(store, 1, known: false, status: MvStatus.Active).ConfigureAwait(false);
        await RegisterAsync(store, 2, known: true).ConfigureAwait(false);
        await store.SetActiveAsync(ServiceId, ViewName, 2).ConfigureAwait(false);
        var pointerBefore = await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false);
        var candidateBefore = await store.GetEntriesAsync(ServiceId, ViewName, 1).ConfigureAwait(false);
        var currentBefore = await store.GetEntriesAsync(ServiceId, ViewName, 2).ConfigureAwait(false);

        var result = await coordinator.ForceReverseAsync(
            new Host(1),
            2,
            1,
            "stale active candidate must remain rejected",
            ServiceId).ConfigureAwait(false);

        Assert.False(result.Succeeded);
        Assert.Equal(MvActivationFailureReason.UnsafeLifecycle, result.FailureReason);
        Assert.Equal(pointerBefore, await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false));
        Assert.Equal(candidateBefore, await store.GetEntriesAsync(ServiceId, ViewName, 1).ConfigureAwait(false));
        Assert.Equal(currentBefore, await store.GetEntriesAsync(ServiceId, ViewName, 2).ConfigureAwait(false));
    }

    public static async Task AssertProviderBoundaryRejectsActiveLifecycleAsync(MultiProviderFixtureBase fixture)
    {
        var (store, _) = await PrepareAsync(fixture).ConfigureAwait(false);
        await RegisterAsync(store, 1, known: false, status: MvStatus.Active).ConfigureAwait(false);
        await RegisterAsync(store, 2, known: true).ConfigureAwait(false);
        await store.SetActiveAsync(ServiceId, ViewName, 2).ConfigureAwait(false);
        var pointerBefore = await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false);
        var candidateBefore = await store.GetEntriesAsync(ServiceId, ViewName, 1).ConfigureAwait(false);
        var currentBefore = await store.GetEntriesAsync(ServiceId, ViewName, 2).ConfigureAwait(false);
        var request = new MvForcedReverseRequest(
            ServiceId,
            ViewName,
            1,
            2,
            1,
            2,
            MvStatus.Active,
            "provider boundary must reject active lifecycle",
            DateTimeOffset.UtcNow);

        var result = await store.TryForceReverseAsync(request).ConfigureAwait(false);

        Assert.False(result.Succeeded);
        Assert.Equal(MvActivationFailureReason.UnsafeLifecycle, result.FailureReason);
        Assert.Equal(pointerBefore, await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false));
        Assert.Equal(candidateBefore, await store.GetEntriesAsync(ServiceId, ViewName, 1).ConfigureAwait(false));
        Assert.Equal(currentBefore, await store.GetEntriesAsync(ServiceId, ViewName, 2).ConfigureAwait(false));
    }

    public static async Task AssertProviderBoundaryExactIdentityAsync(MultiProviderFixtureBase fixture)
    {
        var (store, _) = await PrepareAsync(fixture).ConfigureAwait(false);
        const string otherService = "multi-provider-service-decoy";
        const string otherView = "GenerationViewDecoy";

        await RegisterAsync(store, 0, known: false).ConfigureAwait(false);
        await RegisterAsync(store, 1, known: false).ConfigureAwait(false);
        await RegisterAsync(store, 2, known: true).ConfigureAwait(false);
        await store.SetActiveAsync(ServiceId, ViewName, 2).ConfigureAwait(false);
        await RegisterAsync(store, 1, known: false, serviceId: otherService).ConfigureAwait(false);
        await RegisterAsync(store, 2, known: true, serviceId: otherService).ConfigureAwait(false);
        await store.SetActiveAsync(otherService, ViewName, 2).ConfigureAwait(false);
        await RegisterAsync(store, 1, known: false, viewName: otherView).ConfigureAwait(false);
        await RegisterAsync(store, 2, known: true, viewName: otherView).ConfigureAwait(false);
        await store.SetActiveAsync(ServiceId, otherView, 2).ConfigureAwait(false);

        var otherVersionBefore = await store.GetEntriesAsync(ServiceId, ViewName, 0).ConfigureAwait(false);
        var otherServiceCandidateBefore = await store.GetEntriesAsync(otherService, ViewName, 1).ConfigureAwait(false);
        var otherServiceCurrentBefore = await store.GetEntriesAsync(otherService, ViewName, 2).ConfigureAwait(false);
        var otherServicePointerBefore = await store.GetActiveAsync(otherService, ViewName).ConfigureAwait(false);
        var otherViewCandidateBefore = await store.GetEntriesAsync(ServiceId, otherView, 1).ConfigureAwait(false);
        var otherViewCurrentBefore = await store.GetEntriesAsync(ServiceId, otherView, 2).ConfigureAwait(false);
        var otherViewPointerBefore = await store.GetActiveAsync(ServiceId, otherView).ConfigureAwait(false);
        var request = new MvForcedReverseRequest(
            ServiceId,
            ViewName,
            1,
            2,
            1,
            2,
            MvStatus.Ready,
            "exact provider identity predicates",
            DateTimeOffset.UtcNow);

        var result = await store.TryForceReverseAsync(request).ConfigureAwait(false);

        Assert.True(result.Succeeded, result.Message);
        var intendedPointer = Assert.IsType<MvActiveEntry>(
            await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false));
        Assert.Equal(1, intendedPointer.ActiveVersion);
        Assert.Equal(2, intendedPointer.Generation);
        Assert.All(
            await store.GetEntriesAsync(ServiceId, ViewName, 1).ConfigureAwait(false),
            entry => Assert.Equal(MvStatus.Active, entry.Status));
        Assert.All(
            await store.GetEntriesAsync(ServiceId, ViewName, 2).ConfigureAwait(false),
            entry => Assert.Equal(MvStatus.Ready, entry.Status));
        Assert.Equal(otherVersionBefore, await store.GetEntriesAsync(ServiceId, ViewName, 0).ConfigureAwait(false));
        Assert.Equal(otherServiceCandidateBefore, await store.GetEntriesAsync(otherService, ViewName, 1).ConfigureAwait(false));
        Assert.Equal(otherServiceCurrentBefore, await store.GetEntriesAsync(otherService, ViewName, 2).ConfigureAwait(false));
        Assert.Equal(otherServicePointerBefore, await store.GetActiveAsync(otherService, ViewName).ConfigureAwait(false));
        Assert.Equal(otherViewCandidateBefore, await store.GetEntriesAsync(ServiceId, otherView, 1).ConfigureAwait(false));
        Assert.Equal(otherViewCurrentBefore, await store.GetEntriesAsync(ServiceId, otherView, 2).ConfigureAwait(false));
        Assert.Equal(otherViewPointerBefore, await store.GetActiveAsync(ServiceId, otherView).ConfigureAwait(false));
    }

    public static async Task AssertForcedReverseRaceAsync(MultiProviderFixtureBase fixture)
    {
        var (store, _) = await PrepareAsync(fixture).ConfigureAwait(false);
        await RegisterAsync(store, 1, known: false).ConfigureAwait(false);
        await RegisterAsync(store, 2, known: true).ConfigureAwait(false);
        await store.SetActiveAsync(ServiceId, ViewName, 2).ConfigureAwait(false);
        var request = new MvForcedReverseRequest(
            ServiceId,
            ViewName,
            1,
            2,
            1,
            2,
            MvStatus.Ready,
            "concurrent operator rollback",
            DateTimeOffset.UtcNow);

        var results = await Task.WhenAll(
            store.TryForceReverseAsync(request),
            store.TryForceReverseAsync(request)).ConfigureAwait(false);

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded && result.IsConflict);
        var active = Assert.IsType<MvActiveEntry>(await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false));
        Assert.Equal(1, active.ActiveVersion);
        Assert.Equal(2, active.Generation);
        Assert.Equal(MvSwitchKind.Forced, active.SwitchKind);
    }

    public static async Task AssertForcedReverseCallerRollbackAsync(MultiProviderFixtureBase fixture)
    {
        var (store, _) = await PrepareAsync(fixture).ConfigureAwait(false);
        await RegisterAsync(store, 1, known: false).ConfigureAwait(false);
        await RegisterAsync(store, 2, known: true).ConfigureAwait(false);
        await store.SetActiveAsync(ServiceId, ViewName, 2).ConfigureAwait(false);
        var request = new MvForcedReverseRequest(
            ServiceId,
            ViewName,
            1,
            2,
            1,
            2,
            MvStatus.Ready,
            "transactional operator rollback",
            DateTimeOffset.UtcNow);

        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        await using (var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false))
        {
            var tentative = await store.TryForceReverseAsync(request, transaction).ConfigureAwait(false);
            Assert.True(tentative.Succeeded, tentative.Message);
            await transaction.RollbackAsync().ConfigureAwait(false);
        }

        Assert.Equal(2, (await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false))!.ActiveVersion);
        var retry = await store.TryForceReverseAsync(request).ConfigureAwait(false);
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Equal(1, (await store.GetActiveAsync(ServiceId, ViewName).ConfigureAwait(false))!.ActiveVersion);
    }

    private static async Task<(IMvRegistryStore Store, MvGenerationCoordinator Coordinator)> PrepareAsync(
        MultiProviderFixtureBase fixture)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Integration fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var store = fixture.Services.GetRequiredService<IMvRegistryStore>();
        await store.EnsureInfrastructureAsync().ConfigureAwait(false);
        return (store, CreateCoordinator(fixture, store));
    }

    private static MvGenerationCoordinator CreateCoordinator(
        MultiProviderFixtureBase fixture,
        IMvRegistryStore store,
        MvProjectionStatusPublisher? publisher = null) =>
        new(
            fixture.Executor,
            store,
            Options.Create(new MvOptions { ServiceId = ServiceId }),
            new FixedServiceIdProvider(ServiceId),
            publisher);

    private static async Task RegisterAsync(
        IMvRegistryStore store,
        int version,
        bool known,
        MvStatus status = MvStatus.Ready,
        string serviceId = ServiceId,
        string viewName = ViewName)
    {
        var target = MvCheckpointTruth.KnownZero(MvCheckpointProvenance.AuthoritativeTargetCapture());
        var current = known
            ? MvCheckpointTruth.KnownZero(MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp))
            : MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.ReadUnavailable);
        foreach (var table in new[] { "orders", "items" })
        {
            await store.RegisterAsync(new MvRegistryEntry
            {
                ServiceId = serviceId,
                ViewName = viewName,
                ViewVersion = version,
                LogicalTable = table,
                PhysicalTable = $"{serviceId}_{viewName}_v{version}_{table}",
                Status = status,
                CurrentPosition = current.PositionValue,
                TargetPosition = target.PositionValue,
                CurrentCheckpointTruth = current,
                TargetCheckpointTruth = target,
                LastUpdated = DateTimeOffset.UtcNow
            }).ConfigureAwait(false);
        }
    }

    private sealed class Host(int version) : IMvApplyHost
    {
        public string ViewName => MvGenerationSwitchAssertions.ViewName;
        public int ViewVersion => version;
        public IReadOnlyList<string> LogicalTables => ["orders", "items"];

        public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(IMvTableBindings tables, CancellationToken ct)
        {
            foreach (var logicalTable in LogicalTables)
            {
                tables.RegisterTable(logicalTable);
            }

            return Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);
        }

        public Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
            Sekiban.Dcb.Events.SerializableEvent ev,
            IMvTableBindings tables,
            IMvApplyQueryPort queryPort,
            string sortableUniqueId,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);
    }
}
