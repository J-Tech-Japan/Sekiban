using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G14 operator-only admin surface: <c>ResetProjectionFaultAsync</c>. Drives the REAL grain method on a faulted
///     grain. The reset closes the early-healthy window by itself (in-activation host recreation + first-query barrier),
///     so these tests do NOT deactivate manually except where the point IS a reactivation restore. The three token
///     fields are validated as one atomic precondition against the persisted descriptor inside the single-writer gate;
///     a correct reset also invalidates the derived external snapshot so a rebuild starts from the beginning.
/// </summary>
[Collection("projection-fault-reset")]
public class ProjectionFaultResetOrleansTests : IAsyncLifetime
{
    private static readonly InMemoryEventStore SharedEventStore = new();
    private static readonly InMemoryMultiProjectionStateStore SharedStateStore = new();
    private static readonly FailableStateStore StateStore = new(SharedStateStore);
    private static readonly FaultLifecycleLogProvider FaultLogs = new();
    internal static volatile bool PoisonActive = true;
    private TestCluster _cluster = null!;
    private ISekibanExecutor _executor = null!;
    private IClusterClient Client => _cluster.Client;

    internal static DcbDomainTypes CreateDomain()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<ResetTriggerEvent>();
        var mp = new SimpleMultiProjectorTypes();
        mp.RegisterProjector<ResettableProjector>();
        var q = new SimpleQueryTypes();
        q.RegisterQuery<ResetCountQuery>();
        q.RegisterListQuery<ResetRowListQuery>();
        return new DcbDomainTypes(
            eventTypes,
            new SimpleTagTypes(),
            new SimpleTagProjectorTypes(),
            new SimpleTagStatePayloadTypes(),
            mp,
            q,
            new JsonSerializerOptions());
    }

    public async Task InitializeAsync()
    {
        SharedEventStore.Clear();
        PoisonActive = true;
        await SharedStateStore.DeleteAllAsync(ResettableProjector.MultiProjectorName);
        TogglableGrainStorage.Reset();
        StateStore.ResetForTest();
        ResettableProjector.ResetApplicationObservation();
        FaultLogs.Reset();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var id = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"ResetCluster-{id}";
        builder.Options.ServiceId = $"ResetService-{id}";
        // Avoid TestCluster's dynamic port scanner, which is unreliable on macOS hosts with a large listener table.
        // The collection below makes this per-process pair exclusive across the class's real-cluster tests.
        var portBase = 46_000 + (Environment.ProcessId % 3_000) * 2;
        builder.PortAllocator = new FixedPortAllocator(portBase, portBase + 1);
        builder.Options.BaseSiloPort = portBase;
        builder.Options.BaseGatewayPort = portBase + 1;
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
        _executor = new OrleansDcbExecutor(Client, SharedEventStore, CreateDomain());
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.StopAllSilosAsync();
        }
    }

    private static SerializableEvent Event(bool poison, long tick, Guid id) =>
        new(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new ResetTriggerEvent(poison))),
            new SortableUniqueId(SortableUniqueId.GetTickString(tick) + SortableUniqueId.GetIdString(Guid.Empty)).Value,
            id,
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
            [],
            nameof(ResetTriggerEvent));

    private static async Task InjectExternalSnapshotAsync(string position)
    {
        var request = new MultiProjectionStateWriteRequest(
            ResettableProjector.MultiProjectorName,
            ResettableProjector.MultiProjectorVersion,
            nameof(ResettableProjector),
            position,
            EventsProcessed: 1,
            IsOffloaded: false,
            OffloadKey: null,
            OffloadProvider: null,
            OriginalSizeBytes: 4,
            CompressedSizeBytes: 4,
            SafeWindowThreshold: position,
            CreatedAt: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            BuildSource: "test",
            BuildHost: null);
        using var payload = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        Assert.True((await SharedStateStore.UpsertFromStreamAsync(request, payload, 1_000_000)).IsSuccess);
    }

    private static async Task PollUntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 60; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException("condition not met within the poll window");
    }

    private async Task<IMultiProjectionGrain> ReactivateAsync(IMultiProjectionGrain grain, Func<IMultiProjectionGrain, Task<bool>> until)
    {
        await grain.RequestDeactivationAsync();
        var reactivated = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        await PollUntilAsync(() => until(reactivated));
        return reactivated;
    }

    private async Task<(IMultiProjectionGrain Replacement, MultiProjectionGrainState PersistedFault, int WritesBeforeActivation, int HistoryBeforeActivation)>
        DeactivateWithPersistedFaultVersionAsync(
            IMultiProjectionGrain grain,
            string persistedVersion,
            bool poisonActiveOnReplacement)
    {
        var writesBeforeDeactivate = TogglableGrainStorage.WriteCount;
        await grain.RequestDeactivationAsync();
        await PollUntilAsync(() => Task.FromResult(TogglableGrainStorage.WriteCount > writesBeforeDeactivate));
        Assert.True(TogglableGrainStorage.TryMutatePersistedState(state => state.ProjectorVersion = persistedVersion));
        var persistedFault = Assert.IsType<MultiProjectionGrainState>(TogglableGrainStorage.GetPersistedState());
        Assert.False(string.IsNullOrWhiteSpace(persistedFault.FaultEventId));
        PoisonActive = poisonActiveOnReplacement;
        return (
            Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName),
            persistedFault,
            TogglableGrainStorage.WriteCount,
            TogglableGrainStorage.GetWriteHistory().Count);
    }

    private async Task<(IMultiProjectionGrain Grain, ResetProjectionFaultRequest Token, SerializableEvent Poison)> FaultAndTokenAsync(long tick)
    {
        var id = Guid.CreateVersion7();
        var ev = Event(poison: true, tick, id);
        var grain = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent> { ev });
        await grain.RefreshAsync();
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess); // faulted
        var token = new ResetProjectionFaultRequest(ResettableProjector.MultiProjectorName, id.ToString(), ev.SortableUniqueIdValue);
        return (grain, token, ev);
    }

    private static ResetProjectionFaultRequest TokenFrom(ProjectionFaultInfo info) =>
        new(info.ProjectorName, info.FaultEventId.ToString(), info.Position);

    private static void AssertFaultFieldsOnlyCleared(MultiProjectionGrainState before, MultiProjectionGrainState after)
    {
        Assert.Null(after.FaultEventId);
        Assert.Null(after.FaultEventType);
        Assert.Null(after.FaultPosition);
        Assert.Null(after.FaultMessage);
        Assert.Equal(0, after.FaultedAtUtcTicks);

        // MetadataMaintenance must be a surgical descriptor clear, not a disguised checkpoint/version rewrite.
        Assert.Equal(before.ProjectorName, after.ProjectorName);
        Assert.Equal(before.ProjectorVersion, after.ProjectorVersion);
        Assert.Equal(before.LastSortableUniqueId, after.LastSortableUniqueId);
        Assert.Equal(before.EventsProcessed, after.EventsProcessed);
        Assert.Equal(before.LastPersistTime, after.LastPersistTime);
        Assert.Equal(before.SerializedState, after.SerializedState);
        Assert.Equal(before.StateSize, after.StateSize);
        Assert.Equal(before.SafeLastPosition, after.SafeLastPosition);
        Assert.Equal(before.LastPosition, after.LastPosition);
        Assert.Equal(before.LastGoodSafeVersion, after.LastGoodSafeVersion);
        Assert.Equal(before.LastGoodPayloadBytes, after.LastGoodPayloadBytes);
        Assert.Equal(before.LastGoodOriginalSizeBytes, after.LastGoodOriginalSizeBytes);
        Assert.Equal(before.LastGoodEventsProcessed, after.LastGoodEventsProcessed);
    }

    private static void AssertFaultFieldsEqual(MultiProjectionGrainState expected, MultiProjectionGrainState actual)
    {
        Assert.Equal(expected.FaultEventId, actual.FaultEventId);
        Assert.Equal(expected.FaultEventType, actual.FaultEventType);
        Assert.Equal(expected.FaultPosition, actual.FaultPosition);
        Assert.Equal(expected.FaultMessage, actual.FaultMessage);
        Assert.Equal(expected.FaultedAtUtcTicks, actual.FaultedAtUtcTicks);
    }

    // ---- token validation: each field is part of one atomic precondition; any mismatch rejects with zero effect ----

    [Fact]
    public async Task WrongProjector_IsRejected_NoWrite_FaultRetained()
    {
        var (grain, token, _) = await FaultAndTokenAsync(1_000);
        var writesBefore = TogglableGrainStorage.WriteCount;

        var result = await grain.ResetProjectionFaultAsync(token with { ProjectorName = "some-other-projector" });

        Assert.False(result.IsSuccess);
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task WrongEventId_IsRejected_NoWrite_FaultRetained()
    {
        var (grain, token, _) = await FaultAndTokenAsync(1_100);
        var writesBefore = TogglableGrainStorage.WriteCount;

        var result = await grain.ResetProjectionFaultAsync(token with { FaultEventId = Guid.NewGuid().ToString() });

        Assert.False(result.IsSuccess);
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task WrongPosition_IsRejected_NoWrite_FaultRetained()
    {
        var (grain, token, _) = await FaultAndTokenAsync(1_200);
        var writesBefore = TogglableGrainStorage.WriteCount;

        var result = await grain.ResetProjectionFaultAsync(token with { FaultPosition = "a-different-position" });

        Assert.False(result.IsSuccess);
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task SameTokenRace_AtMostOneSucceeds()
    {
        var (grain, token, _) = await FaultAndTokenAsync(1_300);
        PoisonActive = false;

        var a = grain.ResetProjectionFaultAsync(token);
        var b = grain.ResetProjectionFaultAsync(token);
        var results = await Task.WhenAll(a, b);

        Assert.Equal(1, results.Count(r => r.IsSuccess));
    }

    // ---- SEK-G39 admin-read tokens: one committed descriptor is the sole authority for reset identity ----

    [Fact]
    public async Task AdminReadToken_RoundTripsToReset_AndTheLegacyExceptionDataTokenStillWorks()
    {
        var (grain, _, _) = await FaultAndTokenAsync(1_500);

        var read = await grain.TryGetProjectionFaultAsync();
        Assert.True(read.IsSuccess);
        Assert.True(read.GetValue().HasFault);
        var info = Assert.IsType<ProjectionFaultInfo>(read.GetValue().Fault);
        var canonicalToken = TokenFrom(info);

        // The operator can use the admin-read identity verbatim. Once the underlying poison is fixed, it clears and
        // rebuilds successfully.
        PoisonActive = false;
        Assert.True((await grain.ResetProjectionFaultAsync(canonicalToken)).IsSuccess);
        Assert.True((await grain.GetSnapshotJsonAsync()).IsSuccess); // fold A before enabling the distinct B poison

        // Keep the simpler read-clear-resubmit case separate from the A-to-B race below: the old token is now stale and
        // must not turn a healthy state into a second reset/write.
        var writesBeforeResubmit = TogglableGrainStorage.WriteCount;
        var deletesBeforeResubmit = StateStore.DeleteCount;
        Assert.False((await grain.ResetProjectionFaultAsync(canonicalToken)).IsSuccess);
        Assert.Equal(writesBeforeResubmit, TogglableGrainStorage.WriteCount);
        Assert.Equal(deletesBeforeResubmit, StateStore.DeleteCount);

        // The historical exception-data extraction remains compatible. Establish a new fault so the descriptor is
        // durable, ask again to receive its reconstructed exception, and use the four legacy annotations as the token.
        PoisonActive = true;
        var next = Event(poison: true, tick: 1_501, Guid.CreateVersion7());
        await grain.SeedEventsAsync(new List<SerializableEvent> { next });
        await Assert.ThrowsAnyAsync<Exception>(() => grain.RefreshAsync());
        var reRaised = (await grain.GetSnapshotJsonAsync()).GetException();
        var legacyOperation = Assert.IsType<string>(reRaised.Data[ProjectionFaultDescriptor.OperationDataKey]);
        var legacyEventId = Assert.IsType<string>(reRaised.Data[ProjectionFaultDescriptor.EventIdDataKey]);
        var legacyPosition = Assert.IsType<string>(reRaised.Data[ProjectionFaultDescriptor.PositionDataKey]);
        var legacyToken = new ResetProjectionFaultRequest(
            legacyOperation.Replace("MultiProjection.Fold (", string.Empty, StringComparison.Ordinal)
                .TrimEnd(')'),
            legacyEventId,
            legacyPosition);

        PoisonActive = false;
        Assert.True((await grain.ResetProjectionFaultAsync(legacyToken)).IsSuccess);
    }

    [Fact]
    public async Task AdminReadTokenA_DoesNotClearDurablyPersistedFaultB_ThenTokenBResets()
    {
        var (grain, _, _) = await FaultAndTokenAsync(1_600);
        var readA = await grain.TryGetProjectionFaultAsync();
        Assert.True(readA.IsSuccess);
        var tokenA = TokenFrom(Assert.IsType<ProjectionFaultInfo>(readA.GetValue().Fault));

        // Step 2: reset A after the projector is fixed.
        PoisonActive = false;
        Assert.True((await grain.ResetProjectionFaultAsync(tokenA)).IsSuccess);
        Assert.True((await grain.GetSnapshotJsonAsync()).IsSuccess); // A is durably rebuilt before B is introduced

        // Step 3: establish and durably persist B under the same projection.
        PoisonActive = true;
        var eventB = Event(poison: true, tick: 1_601, Guid.CreateVersion7());
        await grain.SeedEventsAsync(new List<SerializableEvent> { eventB });
        await Assert.ThrowsAnyAsync<Exception>(() => grain.RefreshAsync());
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
        var readB = await grain.TryGetProjectionFaultAsync();
        Assert.True(readB.IsSuccess);
        var infoB = Assert.IsType<ProjectionFaultInfo>(readB.GetValue().Fault);
        Assert.Equal(eventB.Id, infoB.FaultEventId);

        // Step 4: stale A is rejected before either state mutation or external invalidation. Step 5: B remains exactly
        // the committed descriptor an operator would read now.
        var writesBeforeStaleA = TogglableGrainStorage.WriteCount;
        var deletesBeforeStaleA = StateStore.DeleteCount;
        Assert.False((await grain.ResetProjectionFaultAsync(tokenA)).IsSuccess);
        Assert.Equal(writesBeforeStaleA, TogglableGrainStorage.WriteCount);
        Assert.Equal(deletesBeforeStaleA, StateStore.DeleteCount);
        var afterStaleA = await grain.TryGetProjectionFaultAsync();
        Assert.True(afterStaleA.IsSuccess);
        var retainedB = Assert.IsType<ProjectionFaultInfo>(afterStaleA.GetValue().Fault);
        Assert.Equal(infoB, retainedB);

        // Step 6: the fresh B token is the only one accepted.
        PoisonActive = false;
        Assert.True((await grain.ResetProjectionFaultAsync(TokenFrom(retainedB))).IsSuccess);
    }

    [Fact]
    public async Task MalformedCommittedFault_AdminReadReturnsReadFailure_NotUnsupportedOrNoFault()
    {
        var (grain, _, _) = await FaultAndTokenAsync(1_700);
        var reactivated = await CorruptPersistedDescriptorAndReactivateAsync(
            grain,
            state => state.FaultedAtUtcTicks = long.MaxValue,
            poisonActiveOnRestore: false);

        var read = await reactivated.TryGetProjectionFaultAsync();

        Assert.False(read.IsSuccess);
        Assert.NotNull(read.GetException());
        Assert.IsNotType<NotSupportedException>(read.GetException());
        Assert.Contains("ArgumentOutOfRangeException", read.GetException().Message, StringComparison.Ordinal);
    }

    // ---- SEK-G39 version scoping: a stale-version descriptor is one durable clear before any replacement work ----

    [Theory]
    [InlineData("0.9")] // bump: persisted A -> running B (1.0)
    [InlineData("2.0")] // revert: persisted A -> running B (1.0)
    public async Task VersionMismatch_ClearsExactlyTheDurableFaultBeforeReplacementApplies_AndNeverReturns(string persistedVersion)
    {
        var (faulted, _, _) = await FaultAndTokenAsync(persistedVersion == "0.9" ? 1_800 : 1_801);
        var transition = await DeactivateWithPersistedFaultVersionAsync(
            faulted,
            persistedVersion,
            poisonActiveOnReplacement: false);
        ResettableProjector.StartApplicationObservation();

        // Activating through the admin RPC is intentional: it proves the committed no-fault result is not observable
        // until the mismatch clear has committed. Orleans turn scheduling keeps catch-up behind this request.
        var admin = await transition.Replacement.TryGetProjectionFaultAsync();
        Assert.True(admin.IsSuccess);
        Assert.False(admin.GetValue().HasFault);
        Assert.Null(admin.GetValue().Fault);
        Assert.Equal(transition.WritesBeforeActivation + 1, TogglableGrainStorage.WriteCount);

        var writesAfterAdmin = TogglableGrainStorage.GetWriteHistory();
        Assert.Equal(transition.HistoryBeforeActivation + 1, writesAfterAdmin.Count);
        var durableClear = writesAfterAdmin[^1];
        AssertFaultFieldsOnlyCleared(transition.PersistedFault, durableClear);

        var audit = Assert.Single(FaultLogs.Entries, entry => entry.EventId.Name == "ProjectionFaultVersionCleared");
        Assert.Equal(1027, audit.EventId.Id);
        Assert.Contains(persistedVersion, audit.Message, StringComparison.Ordinal);
        Assert.Contains(ResettableProjector.MultiProjectorVersion, audit.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FaultLogs.Entries, entry => entry.EventId.Name == "ProjectionFaultVersionClearFailed");

        // B can now fold the formerly poisoned event. The observation runs inside the REAL projector and reads the
        // test provider's committed record: at B's first apply, precisely the one transition write had landed and the
        // same durable collection held the five cleared fields.
        Assert.True((await transition.Replacement.GetSnapshotJsonAsync()).IsSuccess);
        await PollUntilAsync(() => Task.FromResult(ResettableProjector.FirstApplicationObserved));
        Assert.Equal(transition.WritesBeforeActivation + 1, ResettableProjector.WriteCountAtFirstApplication);
        Assert.True(ResettableProjector.FirstApplicationSawFaultFieldsCleared);

        // A second B activation reads the provider state afresh. If the first clear were in-memory only, the old fault
        // would reappear here; instead the real proxy still observes supported+no-fault.
        var writesBeforeSecondB = TogglableGrainStorage.WriteCount;
        var clearsBeforeSecondB = FaultLogs.Entries.Count(entry => entry.EventId.Name == "ProjectionFaultVersionCleared");
        await transition.Replacement.RequestDeactivationAsync();
        await PollUntilAsync(() => Task.FromResult(TogglableGrainStorage.WriteCount > writesBeforeSecondB));
        var secondB = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        var secondRead = await secondB.TryGetProjectionFaultAsync();
        Assert.True(secondRead.IsSuccess);
        Assert.False(secondRead.GetValue().HasFault);
        Assert.Null(secondRead.GetValue().Fault);
        Assert.Equal(clearsBeforeSecondB, FaultLogs.Entries.Count(entry => entry.EventId.Name == "ProjectionFaultVersionCleared"));
    }

    [Fact]
    public async Task SameVersion_ReactivationRestoresTheFaultAndContinuesFailingClosed()
    {
        var (faulted, _, _) = await FaultAndTokenAsync(1_900);
        var transition = await DeactivateWithPersistedFaultVersionAsync(
            faulted,
            ResettableProjector.MultiProjectorVersion,
            poisonActiveOnReplacement: true);

        var admin = await transition.Replacement.TryGetProjectionFaultAsync();

        Assert.True(admin.IsSuccess);
        Assert.True(admin.GetValue().HasFault);
        Assert.Equal(transition.PersistedFault.FaultEventId, Assert.IsType<ProjectionFaultInfo>(admin.GetValue().Fault).FaultEventId.ToString());
        Assert.Equal(transition.WritesBeforeActivation, TogglableGrainStorage.WriteCount);
        Assert.DoesNotContain(FaultLogs.Entries, entry => entry.EventId.Name == "ProjectionFaultVersionCleared");
        Assert.False((await transition.Replacement.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task VersionMismatch_ClearWriteFailureFailsActivationBeforeApplyOrSuccessfulAdminRead()
    {
        var (faulted, _, _) = await FaultAndTokenAsync(2_000);
        var transition = await DeactivateWithPersistedFaultVersionAsync(
            faulted,
            "0.9",
            poisonActiveOnReplacement: false);
        ResettableProjector.StartApplicationObservation();
        TogglableGrainStorage.FailNextWrite = true;

        await Assert.ThrowsAnyAsync<Exception>(() => transition.Replacement.TryGetProjectionFaultAsync());

        Assert.Equal(transition.WritesBeforeActivation, TogglableGrainStorage.WriteCount);
        Assert.Equal(transition.HistoryBeforeActivation, TogglableGrainStorage.GetWriteHistory().Count);
        var retained = Assert.IsType<MultiProjectionGrainState>(TogglableGrainStorage.GetPersistedState());
        AssertFaultFieldsEqual(transition.PersistedFault, retained);
        Assert.False(ResettableProjector.FirstApplicationObserved);
        var failed = Assert.Single(FaultLogs.Entries, entry => entry.EventId.Name == "ProjectionFaultVersionClearFailed");
        Assert.Equal(1028, failed.EventId.Id);
        Assert.NotNull(failed.Exception);
        Assert.DoesNotContain(FaultLogs.Entries, entry => entry.EventId.Name == "ProjectionFaultVersionCleared");
    }

    // ---- reset semantics: the reset ALONE closes the early-healthy window (no manual deactivation) ----

    [Fact]
    public async Task CorrectToken_FoldableAfterFix_RebuildsImmediately_ExactRecovery_NoEarlyHealthyWindow()
    {
        var (grain, token, poison) = await FaultAndTokenAsync(2_000);
        PoisonActive = false; // the event now folds cleanly

        Assert.True((await grain.ResetProjectionFaultAsync(token)).IsSuccess);

        // No manual deactivation: the very next query on the SAME grain rebuilds via the barrier before answering.
        var count = await _executor.QueryAsync(new ResetCountQuery());
        Assert.True(count.IsSuccess);
        Assert.Equal(1, count.GetValue().Count);                                   // exact scalar value

        var list = await _executor.QueryAsync(new ResetRowListQuery());
        Assert.True(list.IsSuccess);
        Assert.Single(list.GetValue().Items);                                      // exact list count

        // The state snapshot serialized successfully (recovered, no fault) and reflects the recovered position.
        var snapshot = await grain.GetSnapshotJsonAsync();
        Assert.True(snapshot.IsSuccess);
        Assert.Contains(poison.SortableUniqueIdValue, snapshot.GetValue());
    }

    [Fact]
    public async Task CorrectToken_PoisonRemains_RebuildReFaultsImmediately_QueriesRejected_DescriptorPersisted()
    {
        var (grain, token, _) = await FaultAndTokenAsync(3_000);

        Assert.True((await grain.ResetProjectionFaultAsync(token)).IsSuccess);

        // Poison is still poison: the immediate rebuild re-encounters it and re-faults, on the SAME grain.
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetRowListQuery())).IsSuccess);

        // The re-fault is persisted: a genuine fresh activation restores it and fails closed.
        var reactivated = await ReactivateAsync(grain, async g => !(await g.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await reactivated.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task FailFirstPersistedClearWrite_QueriesRemainRejected_ThenRetrySucceeds_ExactRecovery()
    {
        var (grain, token, _) = await FaultAndTokenAsync(4_000);
        PoisonActive = false;

        TogglableGrainStorage.FailNextWrite = true;
        Assert.False((await grain.ResetProjectionFaultAsync(token)).IsSuccess);

        // Before storage recovery: descriptor + live fault retained, every surface rejected.
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetRowListQuery())).IsSuccess);

        // Retry the SAME correct token through the real method: reset commits and rebuilds immediately.
        Assert.True((await grain.ResetProjectionFaultAsync(token)).IsSuccess);
        var count = await _executor.QueryAsync(new ResetCountQuery());
        Assert.True(count.IsSuccess);
        Assert.Equal(1, count.GetValue().Count);
    }

    // ---- external snapshot invalidation: a full rebuild cannot restore a pre-poison derived snapshot ----

    [Theory]
    [InlineData(0, false)] // empty projector
    [InlineData(1, false)] // empty event id
    [InlineData(2, false)] // empty position
    [InlineData(0, true)]  // null projector
    [InlineData(1, true)]  // null event id
    [InlineData(2, true)]  // null position
    public async Task MissingOrEmptyTokenField_IsRejected_NoEffect(int field, bool useNull)
    {
        var (grain, token, _) = await FaultAndTokenAsync(1_400 + field + (useNull ? 100 : 0));
        var writesBefore = TogglableGrainStorage.WriteCount;
        var snapshotBefore = (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue;

        // Only the field under test is missing (null or empty); the other two carry the real token value.
        var missing = useNull ? null! : "";
        var request = new ResetProjectionFaultRequest(
            field == 0 ? missing : token.ProjectorName,
            field == 1 ? missing : token.FaultEventId,
            field == 2 ? missing : token.FaultPosition);

        var result = await grain.ResetProjectionFaultAsync(request);

        Assert.False(result.IsSuccess);                                   // rejected
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);     // zero provider write
        Assert.Equal(snapshotBefore, (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue); // zero external delete
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);     // fault retained
    }

    // ---- Blocker 3/4: descriptor persists on the production RefreshAsync path (no timing assistance); a GENUINE
    // product-persisted pre-poison snapshot restores before the poison, then is invalidated by the reset ----

    [Fact]
    public async Task GenuineExternalSnapshot_RestoresHealthy_ThenPoisonFaultsOnProductionPath_ResetInvalidatesAndRebuildsExactly()
    {
        var healthy = Event(poison: false, tick: 6_000, Guid.CreateVersion7());
        var poison = Event(poison: true, tick: 6_001, Guid.CreateVersion7());
        var grain = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent> { healthy });
        await grain.RefreshAsync();

        // A GENUINE derived snapshot is persisted by the product (not synthetic bytes).
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        var snap = (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue();
        Assert.True(snap.HasValue);
        Assert.Equal(healthy.SortableUniqueIdValue, snap.GetValue().LastSortableUniqueId); // pre-poison position

        // It genuinely restores healthy before the poison: a fresh activation comes up healthy at Count 1.
        var restored = await ReactivateAsync(grain, async g => (await g.GetSnapshotJsonAsync()).IsSuccess);
        Assert.Equal(1, (await _executor.QueryAsync(new ResetCountQuery())).GetValue().Count);

        // Now the poison arrives. A fresh activation restores the pre-poison snapshot, and its FIRST query
        // synchronously catches up through the poison via the barrier, folds it, faults, and persists the descriptor
        // in RefreshAsync's finally — the first query fails without any timing assistance.
        await restored.SeedEventsAsync(new List<SerializableEvent> { poison });
        var faulted = await ReactivateAsync(restored, async g => !(await g.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await faulted.GetSnapshotJsonAsync()).IsSuccess); // faulted, descriptor persisted on the production path

        // The fix now lets the projector fold the previously-poison event; reset with the correct token.
        PoisonActive = false;
        var token = new ResetProjectionFaultRequest(ResettableProjector.MultiProjectorName, poison.Id.ToString(), poison.SortableUniqueIdValue);
        Assert.True((await faulted.ResetProjectionFaultAsync(token)).IsSuccess); // token validated against the PERSISTED descriptor

        // The genuine pre-poison snapshot is invalidated — a stale restore is now impossible.
        Assert.False((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue);

        // Exact rebuild from the beginning: both events fold now (Count 2), position advanced to the poison.
        var count = await _executor.QueryAsync(new ResetCountQuery());
        Assert.True(count.IsSuccess);
        Assert.Equal(2, count.GetValue().Count);
        var list = await _executor.QueryAsync(new ResetRowListQuery());
        Assert.True(list.IsSuccess);
        Assert.Equal(2, list.GetValue().Items.Count());
        var rebuilt = await faulted.GetSnapshotJsonAsync();
        Assert.True(rebuilt.IsSuccess);
        Assert.Contains(poison.SortableUniqueIdValue, rebuilt.GetValue());
    }

    // ---- cross-store partial failures: external-first ordering keeps every outcome retry-coherent ----

    [Fact]
    public async Task ExternalInvalidationFails_ResetRejected_NothingChanged_ThenRetrySucceeds()
    {
        var (grain, token, _) = await FaultAndTokenAsync(7_000);
        await InjectExternalSnapshotAsync("000000000000000700000000000000"); // a snapshot exists to observe retention
        var snapshotBefore = (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue;
        var writesBefore = TogglableGrainStorage.WriteCount;

        // The external invalidation runs first, under the gate, before the grain write. If it fails, the grain write is
        // skipped: descriptor, live fault and the external snapshot are all retained.
        StateStore.FailNextDelete = true;
        Assert.False((await grain.ResetProjectionFaultAsync(token)).IsSuccess);
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);                 // zero grain write
        Assert.Equal(snapshotBefore, (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue); // snapshot retained
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);                 // still faulted

        // Storage recovers: the same token retries and completes (poison fixed so the rebuild recovers).
        PoisonActive = false;
        Assert.True((await grain.ResetProjectionFaultAsync(token)).IsSuccess);
        Assert.False((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue);
    }

    [Fact]
    public async Task GrainWriteFailsAfterExternalDelete_DescriptorAndFaultRetained_SnapshotDeleted_ThenRetrySucceeds()
    {
        var (grain, token, _) = await FaultAndTokenAsync(7_100);
        await InjectExternalSnapshotAsync("000000000000000710000000000000");
        Assert.True((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue);

        // The external delete succeeds, then the grain-state clear write fails: the grain state rolls back (descriptor
        // and live fault retained, queries stay faulted), but the external snapshot is already gone. Coherent — a
        // rebuild would regenerate the snapshot; the retained descriptor keeps the projection fail-closed until retry.
        TogglableGrainStorage.FailNextWrite = true;
        Assert.False((await grain.ResetProjectionFaultAsync(token)).IsSuccess);
        Assert.False((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue); // snapshot deleted
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);                 // descriptor retained -> still faulted

        // Retry the same token: the reset completes and rebuilds.
        PoisonActive = false;
        Assert.True((await grain.ResetProjectionFaultAsync(token)).IsSuccess);
        Assert.Equal(1, (await _executor.QueryAsync(new ResetCountQuery())).GetValue().Count);
    }

    [Fact]
    public async Task NormalPersist_WhileFaulted_WritesNoExternalSnapshot_AdvancesNoMetadata()
    {
        // A faulted projection makes no safe-checkpoint progress AND the external upsert is fault-gated, so a normal
        // persist reaches NO external upsert (the store delegate is never invoked) and advances no persisted snapshot —
        // there is no upsert to race the reset's delete. Asserting the upsert count is unchanged (not merely "no
        // snapshot exists") makes the rejection non-vacuous, and the projection stays faulted afterwards.
        var (grain, _, _) = await FaultAndTokenAsync(8_000);

        var upsertsBefore = StateStore.UpsertCount;
        Assert.True((await grain.PersistStateAsync()).IsSuccess);         // succeeds by skip, not by writing
        Assert.Equal(upsertsBefore, StateStore.UpsertCount);             // no external upsert reached the store
        Assert.False((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue);
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);     // still faulted
    }

    [Fact]
    public async Task VersionRewrite_WhileFaulted_IsRejectedAtFaultGate_NoUpsert_NoVersionWrite_SnapshotRetained()
    {
        // Build + persist cleanly first, so the committed ProjectorVersion is a real "1.0" and a genuine v1.0 external
        // snapshot exists. This is what makes the rewrite below NON-vacuous: OverwritePersistedStateVersionAsync finds
        // a source snapshot and a non-empty current version, so it actually REACHES the coordinated upsert instead of
        // short-circuiting on a missing source.
        var grain = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent> { Event(poison: false, 8_100, Guid.CreateVersion7()) });
        await grain.RefreshAsync();
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        await PollUntilAsync(async () =>
            (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, "1.0")).GetValue().HasValue);

        // Now fault the projection with a poison event.
        await grain.SeedEventsAsync(new List<SerializableEvent> { Event(poison: true, 8_150, Guid.CreateVersion7()) });
        await grain.RefreshAsync();
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess); // faulted

        // The rewrite reaches the coordinated upsert, which returns a fault-blocked Error. So: updated stays false, the
        // store's upsert delegate is never invoked, NO MetadataMaintenance projector-version write happens, no new-version
        // snapshot is created, and the original v1.0 snapshot is retained.
        var upsertsBefore = StateStore.UpsertCount;
        var writesBefore = TogglableGrainStorage.WriteCount;

        var updated = await grain.OverwritePersistedStateVersionAsync("2.0");

        Assert.False(updated);
        Assert.Equal(upsertsBefore, StateStore.UpsertCount);          // upsert never reached the store
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount); // no projector-version write
        Assert.False((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, "2.0")).GetValue().HasValue); // no new-version snapshot
        Assert.True((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, "1.0")).GetValue().HasValue);  // original retained
    }

    [Fact]
    public async Task DeleteExternalStateAsync_RoutedThroughCoordinator_RemovesTheSnapshot()
    {
        // Path coverage for the public DeleteExternalStateAsync, which now routes its delete through the same
        // coordinator as every other external mutation (no direct-DeleteAsync bypass remains). Same-grain calls are
        // serialised by Orleans non-reentrancy, so the delete-vs-upsert race the coordinator actually guards can only be
        // driven by the interleaving catch-up timer; that serialisation is pinned deterministically by the
        // ExternalStoreCoordinator friend tests (delete-waits-for-upsert and upsert-waits-for-delete). Here we assert the
        // end-to-end effect: a routed delete removes the derived snapshot.
        var grain = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent> { Event(poison: false, 8_200, Guid.CreateVersion7()) });
        await grain.RefreshAsync(); // initialise host so DeleteExternalStateAsync has projector name + version
        await InjectExternalSnapshotAsync("000000000000000820000000000000");
        Assert.True((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue);

        var deleted = await grain.DeleteExternalStateAsync();

        Assert.True(deleted);
        Assert.False((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue);
    }

    // Genuinely persist a well-formed descriptor (the grain is already faulted), then deactivate, wait for the
    // deactivation write to LAND (deterministic via WriteCount — OnDeactivateAsync persists the live descriptor on the
    // way out, so the corruption must land after it or it is silently overwritten), corrupt the PERSISTED descriptor in
    // place, set the poison policy for the coming restore, and hand back a fresh grain reference whose activation will
    // read the corrupted descriptor. Only test-owned grain storage is touched — no production seam.
    private async Task<IMultiProjectionGrain> CorruptPersistedDescriptorAndReactivateAsync(
        IMultiProjectionGrain grain,
        Action<MultiProjectionGrainState> corrupt,
        bool poisonActiveOnRestore)
    {
        var writesBeforeDeactivate = TogglableGrainStorage.WriteCount;
        await grain.RequestDeactivationAsync();
        await PollUntilAsync(() => Task.FromResult(TogglableGrainStorage.WriteCount > writesBeforeDeactivate));
        Assert.True(TogglableGrainStorage.TryMutatePersistedState(corrupt));
        PoisonActive = poisonActiveOnRestore;
        return Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
    }

    // A) Persisted-side (not request-side) malformed descriptor for the two fields that CAN be corrupted while the
    // projection stays faulted: the persisted field is genuinely restored on reactivation, then a well-formed reset
    // token is rejected because the PERSISTED field fails the guard. field: 0 = persisted ProjectorName, 1 = persisted
    // FaultPosition. FaultEventId is excluded on purpose — a null/empty persisted FaultEventId restores NO live fault,
    // so it cannot produce a still-faulted projection (see PersistedFaultEventId_* evidence tests below).
    [Theory]
    [InlineData(0, false)] // persisted ProjectorName = ""
    [InlineData(0, true)]  // persisted ProjectorName = null
    [InlineData(1, false)] // persisted FaultPosition = ""
    [InlineData(1, true)]  // persisted FaultPosition = null
    public async Task MalformedPersistedDescriptorField_ResetRejected_NoEffect_QueriesStillFaulted(int field, bool useNull)
    {
        var (grain, token, _) = await FaultAndTokenAsync(9_500 + field * 10 + (useNull ? 1 : 0));
        await InjectExternalSnapshotAsync("000000000000000950000000000000");

        // Benign poison so the corruption survives: the live fault still re-establishes from the intact FaultEventId
        // (queries keep failing), but a faulted projection makes no persist progress, so nothing overwrites the field.
        var malformed = useNull ? null : string.Empty;
        var reactivated = await CorruptPersistedDescriptorAndReactivateAsync(
            grain,
            s =>
            {
                if (field == 0)
                {
                    s.ProjectorName = malformed!;
                }
                else
                {
                    s.FaultPosition = malformed;
                }
            },
            poisonActiveOnRestore: false);
        await PollUntilAsync(async () => !(await reactivated.GetSnapshotJsonAsync()).IsSuccess); // faulted

        var upsertsBefore = StateStore.UpsertCount;
        var writesBefore = TogglableGrainStorage.WriteCount;
        var snapshotBefore = (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue;

        // The well-formed token cannot match a descriptor whose persisted field is null/empty.
        var reset = await reactivated.ResetProjectionFaultAsync(token);

        Assert.False(reset.IsSuccess);                                                            // rejected
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);                             // zero grain/provider write, zero version change
        Assert.Equal(upsertsBefore, StateStore.UpsertCount);                                      // zero external upsert
        Assert.Equal(snapshotBefore, (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue); // zero external delete (snapshot retained)
        // No live clear / no deactivation: state, scalar and list all continue to fail closed.
        Assert.False((await reactivated.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetRowListQuery())).IsSuccess);
    }

    // B-evidence (benign poison): a null/empty persisted FaultEventId does NOT restore a live fault, because
    // RestoreProjectionFaultIfPersisted needs a PARSEABLE Guid. With the poison benign, the fresh activation therefore
    // comes up HEALTHY — documenting that "malformed persisted FaultEventId + still faulted" is unreachable on this path.
    [Theory]
    [InlineData(false)] // persisted FaultEventId = ""
    [InlineData(true)]  // persisted FaultEventId = null
    public async Task PersistedFaultEventId_MalformedWithBenignPoison_RestoresNoLiveFault_ProjectionHealthy(bool useNull)
    {
        var (grain, _, _) = await FaultAndTokenAsync(9_700 + (useNull ? 1 : 0));
        var malformed = useNull ? null : string.Empty;

        var reactivated = await CorruptPersistedDescriptorAndReactivateAsync(
            grain,
            s => s.FaultEventId = malformed,
            poisonActiveOnRestore: false);

        // No parseable FaultEventId -> no live fault restored -> healthy (the first-query barrier folds the now-benign
        // poison successfully). Queries succeed; there is no faulted state a reset could be issued against.
        await PollUntilAsync(async () => (await reactivated.GetSnapshotJsonAsync()).IsSuccess);
        Assert.True((await _executor.QueryAsync(new ResetCountQuery())).IsSuccess);
    }

    // B-evidence (active poison): with the same malformed persisted FaultEventId but the poison STILL active, the fresh
    // activation restores no fault, so the first-query barrier re-folds the poison and re-establishes a fresh WELL-FORMED
    // live fault (self-heal). Queries fail closed, and once that valid fault is persisted the original well-formed token
    // resets it — so the malformed FaultEventId never becomes a stuck, unresettable descriptor.
    [Theory]
    [InlineData(false)] // persisted FaultEventId = ""
    [InlineData(true)]  // persisted FaultEventId = null
    public async Task PersistedFaultEventId_MalformedWithActivePoison_FirstQuerySelfHealsToValidLiveFault(bool useNull)
    {
        var (grain, token, _) = await FaultAndTokenAsync(9_720 + (useNull ? 1 : 0));
        var malformed = useNull ? null : string.Empty;

        var reactivated = await CorruptPersistedDescriptorAndReactivateAsync(
            grain,
            s => s.FaultEventId = malformed,
            poisonActiveOnRestore: true);

        // Barrier re-folds the still-active poison -> re-faulted with a fresh valid live fault.
        await PollUntilAsync(async () => !(await reactivated.GetSnapshotJsonAsync()).IsSuccess);

        // Persist that valid live fault (OnDeactivateAsync writes it) so the descriptor is self-healed to a valid one,
        // then the original well-formed token resets it — the malformed FaultEventId is gone.
        var writesBeforeDeactivate = TogglableGrainStorage.WriteCount;
        await reactivated.RequestDeactivationAsync();
        await PollUntilAsync(() => Task.FromResult(TogglableGrainStorage.WriteCount > writesBeforeDeactivate));
        var healed = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        await PollUntilAsync(async () => !(await healed.GetSnapshotJsonAsync()).IsSuccess);

        Assert.True((await healed.ResetProjectionFaultAsync(token)).IsSuccess);
    }

    // C) Both-empty FALSE-MATCH: this is the scenario the non-empty guards actually protect against. Corrupt persisted
    // field X to "" AND send a token whose field X is also "" (other fields well-formed and matching). The reset must be
    // rejected; removing the PAIRED request+committed non-empty checks for X would let string.Equals("","") spuriously
    // match and reset. field: 0 = ProjectorName, 1 = FaultEventId, 2 = FaultPosition. Asserts zero delete/write/live.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task BothEmptyTokenAndPersistedField_ResetRejected_NoFalseMatch(int field)
    {
        var (grain, token, _) = await FaultAndTokenAsync(9_800 + field);
        await InjectExternalSnapshotAsync("000000000000000980000000000000");

        // Corrupt only field X to "". For field 1 (FaultEventId) this also means no live fault is restored (healthy),
        // but the persisted empty identity must still never be matched by an empty token.
        var reactivated = await CorruptPersistedDescriptorAndReactivateAsync(
            grain,
            s =>
            {
                if (field == 0)
                {
                    s.ProjectorName = string.Empty;
                }
                else if (field == 1)
                {
                    s.FaultEventId = string.Empty;
                }
                else
                {
                    s.FaultPosition = string.Empty;
                }
            },
            poisonActiveOnRestore: false);
        // Trigger the activation (faulted for field 0/2, healthy for field 1) before probing.
        await reactivated.GetSnapshotJsonAsync();

        // Token that mirrors the empty persisted field X, with the other two fields well-formed and matching.
        var emptyToken = field switch
        {
            0 => new ResetProjectionFaultRequest(string.Empty, token.FaultEventId, token.FaultPosition),
            1 => new ResetProjectionFaultRequest(token.ProjectorName, string.Empty, token.FaultPosition),
            _ => new ResetProjectionFaultRequest(token.ProjectorName, token.FaultEventId, string.Empty)
        };

        var upsertsBefore = StateStore.UpsertCount;
        var writesBefore = TogglableGrainStorage.WriteCount;
        var snapshotBefore = (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue;

        var reset = await reactivated.ResetProjectionFaultAsync(emptyToken);

        Assert.False(reset.IsSuccess);                                                            // no empty-identity false match
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);                             // zero grain/provider write
        Assert.Equal(upsertsBefore, StateStore.UpsertCount);                                      // zero external upsert
        Assert.Equal(snapshotBefore, (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue); // zero external delete
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddLogging(logging => logging.AddProvider(FaultLogs));
                    services.AddSingleton(_ => CreateDomain());
                    services.AddSingleton<IEventStore>(SharedEventStore);
                    services.AddSingleton<IMultiProjectionStateStore>(StateStore);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    services.AddSingleton<Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 1 });
                    services.AddSekibanDcbNativeRuntime();
                    services.AddGrainStorage("OrleansStorage", (_, _) => new TogglableGrainStorage());
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    /// <summary>Delegates to a real in-memory state store, but can fail the next DeleteAsync to model an external-store failure.</summary>
    private sealed class FailableStateStore : IMultiProjectionStateStore
    {
        private readonly IMultiProjectionStateStore _inner;
        public volatile bool FailNextDelete;
        // Counts real upsert-delegate invocations. A fault-gated persist must NEVER reach the store, so an unchanged
        // count across a persist proves rejection at the coordinator — stronger than merely "no v2 snapshot exists".
        private int _upsertCount;
        private int _deleteCount;
        public int UpsertCount => Volatile.Read(ref _upsertCount);
        public int DeleteCount => Volatile.Read(ref _deleteCount);

        public FailableStateStore(IMultiProjectionStateStore inner) => _inner = inner;

        public void ResetForTest()
        {
            FailNextDelete = false;
            Interlocked.Exchange(ref _upsertCount, 0);
            Interlocked.Exchange(ref _deleteCount, 0);
        }

        public Task<ResultBox<bool>> DeleteAsync(string projectorName, string projectorVersion, CancellationToken cancellationToken = default)
        {
            if (FailNextDelete)
            {
                FailNextDelete = false;
                return Task.FromResult(ResultBox.Error<bool>(new InvalidOperationException("injected: external snapshot delete failure")));
            }

            Interlocked.Increment(ref _deleteCount);
            return _inner.DeleteAsync(projectorName, projectorVersion, cancellationToken);
        }

        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(string projectorName, string projectorVersion, CancellationToken cancellationToken = default) =>
            _inner.GetLatestForVersionAsync(projectorName, projectorVersion, cancellationToken);
        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(string projectorName, CancellationToken cancellationToken = default) =>
            _inner.GetLatestAnyVersionAsync(projectorName, cancellationToken);
        public Task<ResultBox<bool>> UpsertAsync(MultiProjectionStateRecord record, int offloadThresholdBytes = 1_000_000, CancellationToken cancellationToken = default) =>
            _inner.UpsertAsync(record, offloadThresholdBytes, cancellationToken);
        public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(CancellationToken cancellationToken = default) =>
            _inner.ListAllAsync(cancellationToken);
        public Task<ResultBox<int>> DeleteAllAsync(string? projectorName = null, CancellationToken cancellationToken = default) =>
            _inner.DeleteAllAsync(projectorName, cancellationToken);
        public Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(MultiProjectionStateRecord record, CancellationToken cancellationToken = default) =>
            _inner.OpenStateDataReadStreamAsync(record, cancellationToken);
        public Task<ResultBox<bool>> UpsertFromStreamAsync(MultiProjectionStateWriteRequest request, Stream stream, int offloadThresholdBytes, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _upsertCount);
            return _inner.UpsertFromStreamAsync(request, stream, offloadThresholdBytes, cancellationToken);
        }
    }

    /// <summary>
    ///     Records the production grain's structured event ids without replacing its logger. The version-transition
    ///     assertions need to distinguish successful audit telemetry from the fail-before-serving telemetry.
    /// </summary>
    private sealed class FaultLifecycleLogProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<RecordedLog> _entries = new();
        public IReadOnlyCollection<RecordedLog> Entries => _entries.ToArray();

        public void Reset()
        {
            while (_entries.TryDequeue(out _))
            {
            }
        }

        public ILogger CreateLogger(string categoryName) => new FaultLifecycleLogger(_entries);
        public void Dispose() { }

        private sealed class FaultLifecycleLogger(ConcurrentQueue<RecordedLog> entries) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new RecordedLog(eventId, exception, formatter(state, exception)));
        }

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed record RecordedLog(EventId EventId, Exception? Exception, string Message);

    /// <summary>An in-memory grain storage that really persists (so reactivation restores) and can fail the next write.</summary>
    private sealed class TogglableGrainStorage : IGrainStorage
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<string, object?> Store = new();
        private static readonly List<MultiProjectionGrainState> WriteHistory = new();
        public static int WriteCount;
        public static bool FailNextWrite;

        public static void Reset()
        {
            lock (Sync)
            {
                Store.Clear();
                WriteHistory.Clear();
                WriteCount = 0;
                FailNextWrite = false;
            }
        }

        /// <summary>Immutable snapshots of actual provider writes, used to prove the transition's write provenance.</summary>
        public static IReadOnlyList<MultiProjectionGrainState> GetWriteHistory()
        {
            lock (Sync)
            {
                return WriteHistory.Select(state => state.Clone()).ToArray();
            }
        }

        /// <summary>Returns the committed provider record rather than an actor-local state reference.</summary>
        public static MultiProjectionGrainState? GetPersistedState()
        {
            lock (Sync)
            {
                return Store.Values.OfType<MultiProjectionGrainState>().SingleOrDefault()?.Clone();
            }
        }

        public Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Sync)
            {
                if (Store.TryGetValue(grainId.ToString(), out var saved) && saved is T typed)
                {
                    grainState.State = typed;
                    grainState.RecordExists = true;
                }
                else
                {
                    grainState.RecordExists = false;
                }
            }

            return Task.CompletedTask;
        }

        public Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Sync)
            {
                if (FailNextWrite)
                {
                    FailNextWrite = false;
                    throw new InvalidOperationException("injected: reset persisted clear write failure");
                }

                Store[grainId.ToString()] = grainState.State;
                if (grainState.State is MultiProjectionGrainState projectionState)
                {
                    WriteHistory.Add(projectionState.Clone());
                }
                WriteCount++;
            }

            return Task.CompletedTask;
        }

        public Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Sync)
            {
                Store.Remove(grainId.ToString());
            }

            return Task.CompletedTask;
        }

        // Test-only seam: corrupt the already-persisted grain state in place, so a subsequent reactivation genuinely
        // RESTORES a malformed fault descriptor (this models a persisted-side corruption, not a request-side one).
        // Returns false when no MultiProjectionGrainState has been persisted yet.
        public static bool TryMutatePersistedState(Action<MultiProjectionGrainState> mutate)
        {
            lock (Sync)
            {
                foreach (var value in Store.Values)
                {
                    if (value is MultiProjectionGrainState state)
                    {
                        mutate(state);
                        return true;
                    }
                }

                return false;
            }
        }
    }

    private sealed class FixedPortAllocator(int baseSiloPort, int baseGatewayPort) : ITestClusterPortAllocator
    {
        public (int, int) AllocateConsecutivePortPairs(int numPorts) => (baseSiloPort, baseGatewayPort);
        public void Dispose() { }
    }

    internal record ResetTriggerEvent(bool Poison) : IEventPayload;

    internal record ResetCountResult(int Count);

    internal record ResetCountQuery : IMultiProjectionQuery<ResettableProjector, ResetCountQuery, ResetCountResult>
    {
        public static ResultBox<ResetCountResult> HandleQuery(
            ResettableProjector projector,
            ResetCountQuery query,
            IQueryContext context) => ResultBox.FromValue(new ResetCountResult(projector.Count));
    }

    internal record ResetRow(int Value);

    internal record ResetRowListQuery :
        IMultiProjectionListQuery<ResettableProjector, ResetRowListQuery, ResetRow>,
        IQueryPagingParameter
    {
        public int? PageNumber { get; init; }
        public int? PageSize { get; init; }

        public static ResultBox<IEnumerable<ResetRow>> HandleFilter(
            ResettableProjector projector,
            ResetRowListQuery query,
            IQueryContext context) => ResultBox.FromValue(Enumerable.Range(0, projector.Count).Select(i => new ResetRow(i)));

        public static ResultBox<IEnumerable<ResetRow>> HandleSort(
            IEnumerable<ResetRow> filtered,
            ResetRowListQuery query,
            IQueryContext context) => ResultBox.FromValue(filtered);
    }

    /// <summary>A projector that folds a poison event only while <see cref="PoisonActive" /> is set — so a test can "fix" it.</summary>
    internal record ResettableProjector : IMultiProjector<ResettableProjector>
    {
        public int Count { get; init; }
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "resettable-fault-projector";
        private static int _observeApplications;
        private static int _firstApplicationRecorded;
        private static int _firstApplicationObserved;
        private static int _firstApplicationSawFaultFieldsCleared;
        private static int _writeCountAtFirstApplication;

        /// <summary>
        ///     Test-only observation of the actual production apply boundary. It reads the same provider record that
        ///     activation restored, so a mutation that clears merely an actor-local fault or moves the write after
        ///     catch-up cannot satisfy the version-transition proof.
        /// </summary>
        public static bool FirstApplicationObserved => Volatile.Read(ref _firstApplicationObserved) != 0;
        public static bool FirstApplicationSawFaultFieldsCleared => Volatile.Read(ref _firstApplicationSawFaultFieldsCleared) != 0;
        public static int WriteCountAtFirstApplication => Volatile.Read(ref _writeCountAtFirstApplication);

        public static void ResetApplicationObservation()
        {
            Interlocked.Exchange(ref _observeApplications, 0);
            Interlocked.Exchange(ref _firstApplicationRecorded, 0);
            Interlocked.Exchange(ref _firstApplicationObserved, 0);
            Interlocked.Exchange(ref _firstApplicationSawFaultFieldsCleared, 0);
            Interlocked.Exchange(ref _writeCountAtFirstApplication, 0);
        }

        public static void StartApplicationObservation()
        {
            ResetApplicationObservation();
            Volatile.Write(ref _observeApplications, 1);
        }

        public static ResettableProjector GenerateInitialPayload() => new();

        public static ResultBox<ResettableProjector> Project(
            ResettableProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold)
        {
            if (Volatile.Read(ref _observeApplications) != 0 && Interlocked.Exchange(ref _firstApplicationRecorded, 1) == 0)
            {
                var committed = TogglableGrainStorage.GetPersistedState();
                Volatile.Write(ref _writeCountAtFirstApplication, TogglableGrainStorage.WriteCount);
                Volatile.Write(
                    ref _firstApplicationSawFaultFieldsCleared,
                    committed is not null &&
                    committed.FaultEventId is null &&
                    committed.FaultEventType is null &&
                    committed.FaultPosition is null &&
                    committed.FaultMessage is null &&
                    committed.FaultedAtUtcTicks == 0
                        ? 1
                        : 0);
                Volatile.Write(ref _firstApplicationObserved, 1);
            }

            if (ev.Payload is ResetTriggerEvent { Poison: true } && PoisonActive)
            {
                throw new InvalidOperationException("poison event: refuses to fold while poison is active");
            }

            return ResultBox.FromValue(payload with { Count = payload.Count + 1 });
        }
    }
}

[CollectionDefinition("projection-fault-reset", DisableParallelization = true)]
public sealed class ProjectionFaultResetCollection { }
