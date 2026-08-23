using ResultBoxes;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using System.Reflection;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Production-surface evidence for SEK-G41. These tests intentionally drive the public wrapper/actor entry points,
///     rather than invoking the private consume helper, so removal of a forwarding path or reintroduction of an
///     arrival-time reconcile is observable.
/// </summary>
public class DualStateProjectionWrapperDeferredRepairTests
{
    [Theory]
    [InlineData("GetSafeProjection")]
    [InlineData("GetUnsafeProjection")]
    [InlineData("PromoteBufferedEventsZero")]
    [InlineData("GetSafeProjectorPayload")]
    [InlineData("GetUnsafeProjectorPayload")]
    [InlineData("CompactSafeHistory")]
    public void DirtyHistory_IsConsumedExactlyOnceThroughEachWrapperProductionEntryPoint(string entryPoint)
    {
        var fixture = new Fixture();
        var wrapper = fixture.CreateWrapper();
        var seed = SeedDirtyHistory(wrapper, fixture.DomainTypes);
        var accessor = (IDualStateAccessor)wrapper;

        // The high event was folded once and cleanly reconciled before the disorder. The two retained out-of-order safe
        // arrivals caused neither an additional fold nor tag resolution; their repair waits for a real consumption.
        Assert.Equal(1, fixture.TagTypes.FoldCount);
        Assert.Equal(1, fixture.TagTypes.TagResolutionCount);
        Assert.True(GetPrivateBool(wrapper, "_safeHistoryDirty"));
        Assert.Equal(1, accessor.SafeVersion);
        Assert.Equal(1, accessor.UnsafeVersion);

        InvokeWrapperEntryPoint(entryPoint, wrapper, accessor, fixture.DomainTypes);

        // Inspect the published fields directly. Calling either payload getter here would itself enter the consumption
        // seam and could hide a missing forwarding call in the entry point under test.
        AssertConsumedState(wrapper, accessor, fixture, seed, expectedFoldCount: 4);
        Assert.False(GetPrivateBool(wrapper, "_safeHistoryDirty"));
        Assert.False(GetPrivateBool(wrapper, "_servedStateDirty"));

        if (entryPoint == "CompactSafeHistory")
        {
            Assert.Equal(0, GetPrivateCount(wrapper, "_allSafeEvents"));
            Assert.True(GetPrivateBool(wrapper, "_useIncrementalSafePromotion"));
        }
        else
        {
            Assert.Equal(3, GetPrivateCount(wrapper, "_allSafeEvents"));
        }
    }

    [Theory]
    [InlineData("GetSafeProjection")]
    [InlineData("GetUnsafeProjection")]
    [InlineData("PromoteBufferedEventsZero")]
    [InlineData("GetSafeProjectorPayload")]
    [InlineData("GetUnsafeProjectorPayload")]
    [InlineData("CompactSafeHistory")]
    public void DirtyHistory_EachWrapperConsumptionEntryPoint_FailsWithoutPublishingAndCanRetry(string entryPoint)
    {
        var fixture = new Fixture();
        var wrapper = fixture.CreateWrapper();
        var seed = SeedDirtyHistory(wrapper, fixture.DomainTypes);
        var accessor = (IDualStateAccessor)wrapper;
        var published = CapturePublishedState(wrapper, accessor);
        // Low made the retained history dirty, but high is the event that fails when the sorted history is replayed.
        // This must never be attributed to the dirty trigger merely because it arrived later.
        fixture.TagTypes.FailDuringFold.Add(seed.High.Id);

        var failure = Assert.Throws<InvalidOperationException>(
            () => InvokeWrapperEntryPoint(entryPoint, wrapper, accessor, fixture.DomainTypes));

        Assert.Equal(seed.High.Id.ToString(), failure.Data["DeferredSafeRepairEventId"]);
        Assert.Equal(seed.High.SortableUniqueIdValue, failure.Data["DeferredSafeRepairPosition"]);
        Assert.Equal(seed.Low.Id.ToString(), failure.Data["DeferredSafeRepairDirtyEventId"]);
        Assert.Equal(seed.Low.SortableUniqueIdValue, failure.Data["DeferredSafeRepairDirtyPosition"]);
        AssertPublishedState(published, wrapper, accessor);
        Assert.True(GetPrivateBool(wrapper, "_safeHistoryDirty"));
        Assert.True(GetPrivateBool(wrapper, "_servedStateDirty"));
        Assert.Equal(3, GetPrivateCount(wrapper, "_allSafeEvents"));
        Assert.False(GetPrivateBool(wrapper, "_useIncrementalSafePromotion"));

        fixture.TagTypes.FailDuringFold.Remove(seed.High.Id);
        InvokeWrapperEntryPoint(entryPoint, wrapper, accessor, fixture.DomainTypes);

        AssertConsumedState(wrapper, accessor, fixture, seed, expectedFoldCount: 7);
        Assert.Equal(entryPoint == "CompactSafeHistory" ? 0 : 3, GetPrivateCount(wrapper, "_allSafeEvents"));
        Assert.Equal(entryPoint == "CompactSafeHistory", GetPrivateBool(wrapper, "_useIncrementalSafePromotion"));
    }

    [Fact]
    public async Task DirtyHistory_IsConsumedThroughTheSeventhProductionActorHeadStatusEntryPoint()
    {
        var fixture = new Fixture();
        var actor = new GeneralMultiProjectionActor(
            fixture.DomainTypes,
            CountingProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1 });
        var seed = CreateSeedEvents();

        // Separate real actor deliveries preserve the disorder; AddEventsAsync's per-batch ordering cannot hide it.
        await actor.AddEventsAsync([seed.High]);
        await actor.AddEventsAsync([seed.Low]);
        await actor.AddEventsAsync([seed.Middle]);
        Assert.Equal(1, fixture.TagTypes.FoldCount);

        var head = await actor.GetProjectionHeadStatusAsync();

        Assert.Equal(3, head.Current.EventVersion);
        Assert.Equal(3, head.Consistent.EventVersion);
        Assert.Equal(seed.High.SortableUniqueIdValue, head.Current.LastSortableUniqueId);
        Assert.Equal(seed.High.SortableUniqueIdValue, head.Consistent.LastSortableUniqueId);
        Assert.Equal(4, fixture.TagTypes.FoldCount);
        Assert.Equal(4, fixture.TagTypes.TagResolutionCount);
    }

    [Fact]
    public async Task DirtyHistory_ActorHeadStatusEntryPoint_FailsWithoutPublishingAndCanRetry()
    {
        var fixture = new Fixture();
        var logger = new RecordingLogger();
        var actor = new GeneralMultiProjectionActor(
            fixture.DomainTypes,
            CountingProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1 },
            logger);
        var seed = CreateSeedEvents();

        await actor.AddEventsAsync([seed.High]);
        await actor.AddEventsAsync([seed.Low]);
        await actor.AddEventsAsync([seed.Middle]);
        var wrapper = Assert.IsType<DualStateProjectionWrapper<CountingProjector>>(
            GetPrivateValue<object>(actor, "_singleStateAccessor"));
        var accessor = (IDualStateAccessor)wrapper;
        var published = CapturePublishedState(wrapper, accessor);
        var original = new InvalidOperationException($"Configured replay fold failure for {seed.High.Id}");
        fixture.TagTypes.ReplayFailures[seed.High.Id] = original;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(actor.GetProjectionHeadStatusAsync);

        Assert.Same(original, failure);
        Assert.Equal(original.Message, failure.Message);
        Assert.Contains("RebuildSafeState", failure.StackTrace, StringComparison.Ordinal);
        Assert.Equal(seed.High.Id.ToString(), failure.Data["DeferredSafeRepairEventId"]);
        Assert.Equal(seed.High.SortableUniqueIdValue, failure.Data["DeferredSafeRepairPosition"]);
        Assert.Equal(seed.Low.Id.ToString(), failure.Data["DeferredSafeRepairDirtyEventId"]);
        Assert.Equal(seed.Low.SortableUniqueIdValue, failure.Data["DeferredSafeRepairDirtyPosition"]);
        var fault = Assert.IsType<ProjectionFaultDescriptor>(actor.CurrentFault);
        Assert.Equal(seed.High.Id, fault.EventId);
        Assert.Equal(nameof(FoldEvent), fault.EventType);
        Assert.Equal(seed.High.SortableUniqueIdValue, fault.Position);
        Assert.Equal(seed.High.Id.ToString(), failure.Data[ProjectionFaultDescriptor.EventIdDataKey]);
        Assert.Equal(seed.High.SortableUniqueIdValue, failure.Data[ProjectionFaultDescriptor.PositionDataKey]);
        var firstObserved = Assert.Single(
            logger.Entries,
            entry => entry.EventId.Name == "ProjectionFaultFirstObserved");
        Assert.Same(original, firstObserved.Exception);
        AssertPublishedState(published, wrapper, accessor);
        Assert.True(GetPrivateBool(wrapper, "_safeHistoryDirty"));
        Assert.Equal(3, GetPrivateCount(wrapper, "_allSafeEvents"));

        // A fault re-raise does not create another first-observed entry. Deliberate recovery leaves the retained dirty
        // history in place, then lets the same actor consume it without dropping high/low/middle.
        _ = await actor.GetStateAsync();
        Assert.Single(logger.Entries, entry => entry.EventId.Name == "ProjectionFaultFirstObserved");
        fixture.TagTypes.ReplayFailures.Remove(seed.High.Id);
        actor.ClearFaultForRebuild();
        var retried = await actor.GetProjectionHeadStatusAsync();
        Assert.Equal(3, retried.Current.EventVersion);
        Assert.Equal(3, retried.Consistent.EventVersion);
        AssertConsumedState(wrapper, accessor, fixture, seed, expectedFoldCount: 7);
    }

    [Fact]
    public async Task DirtyHistory_ActorStateRead_CapturesTheReplayFailingEventWithoutReturningStaleState()
    {
        var fixture = new Fixture();
        var logger = new RecordingLogger();
        var actor = new GeneralMultiProjectionActor(
            fixture.DomainTypes,
            CountingProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1 },
            logger);
        var seed = CreateSeedEvents();

        await actor.AddEventsAsync([seed.High]);
        await actor.AddEventsAsync([seed.Low]);
        await actor.AddEventsAsync([seed.Middle]);
        var wrapper = Assert.IsType<DualStateProjectionWrapper<CountingProjector>>(
            GetPrivateValue<object>(actor, "_singleStateAccessor"));
        var accessor = (IDualStateAccessor)wrapper;
        var published = CapturePublishedState(wrapper, accessor);
        var original = new InvalidOperationException($"Configured state-read replay failure for {seed.High.Id}");
        fixture.TagTypes.ReplayFailures[seed.High.Id] = original;

        var state = await actor.GetStateAsync(canGetUnsafeState: true);

        Assert.False(state.IsSuccess);
        var failure = Assert.IsType<InvalidOperationException>(state.GetException());
        Assert.Same(original, failure);
        Assert.Equal(original.Message, failure.Message);
        Assert.Contains("RebuildSafeState", failure.StackTrace, StringComparison.Ordinal);
        Assert.Equal(seed.High.Id.ToString(), failure.Data["DeferredSafeRepairEventId"]);
        Assert.Equal(seed.High.SortableUniqueIdValue, failure.Data["DeferredSafeRepairPosition"]);
        Assert.Equal(seed.Low.Id.ToString(), failure.Data["DeferredSafeRepairDirtyEventId"]);
        Assert.Equal(seed.Low.SortableUniqueIdValue, failure.Data["DeferredSafeRepairDirtyPosition"]);
        Assert.Equal(seed.High.Id, Assert.IsType<ProjectionFaultDescriptor>(actor.CurrentFault).EventId);
        var firstObserved = Assert.Single(
            logger.Entries,
            entry => entry.EventId.Name == "ProjectionFaultFirstObserved");
        Assert.Same(original, firstObserved.Exception);
        AssertPublishedState(published, wrapper, accessor);
        Assert.True(GetPrivateBool(wrapper, "_safeHistoryDirty"));
        Assert.True(GetPrivateBool(wrapper, "_servedStateDirty"));
        Assert.Equal(3, GetPrivateCount(wrapper, "_allSafeEvents"));

        fixture.TagTypes.ReplayFailures.Remove(seed.High.Id);
        actor.ClearFaultForRebuild();
        var retried = await actor.GetStateAsync(canGetUnsafeState: true);
        Assert.True(retried.IsSuccess);
        Assert.Equal("ABC", Assert.IsType<CountingProjector>(retried.GetValue().Payload).Order);
    }

    [Fact]
    public void DirtyProcessEvent_SuppressesReconcileUntilTheFirstRealConsumeThenReconcilesOnce()
    {
        var fixture = new Fixture();
        var wrapper = fixture.CreateWrapper();
        var accessor = (IDualStateAccessor)wrapper;
        var baseline = DateTime.UtcNow.AddMinutes(-10);
        var threshold = new SortableUniqueId(SortableUniqueId.Generate(baseline.AddSeconds(5), Guid.Empty));
        var low = CreateEvent(new FoldEvent(1, "A"), SortableUniqueId.Generate(baseline, Guid.Empty), GuidFromInt(201));
        var middle = CreateEvent(new FoldEvent(2, "B"), SortableUniqueId.Generate(baseline.AddSeconds(1), Guid.Empty), GuidFromInt(202));
        var high = CreateEvent(new FoldEvent(3, "C"), SortableUniqueId.Generate(baseline.AddSeconds(2), Guid.Empty), GuidFromInt(203));
        var buffered = CreateEvent(new FoldEvent(4, "U"), SortableUniqueId.Generate(baseline.AddSeconds(10), Guid.Empty), GuidFromInt(204));

        wrapper.ProcessEvent(high, threshold, fixture.DomainTypes);     // one direct safe fold and clean reconcile
        wrapper.ProcessEvent(buffered, threshold, fixture.DomainTypes); // one direct unsafe fold
        wrapper.ProcessEvent(low, threshold, fixture.DomainTypes);      // dirty-producing arrival: must not reconcile U
        wrapper.ProcessEvent(middle, threshold, fixture.DomainTypes);   // apparent in-order tail: must remain unfurled

        Assert.True(GetPrivateBool(wrapper, "_safeHistoryDirty"));
        Assert.Equal(2, fixture.TagTypes.FoldCount);
        Assert.Equal(2, fixture.TagTypes.TagResolutionCount);
        Assert.Equal("C", GetPrivateProjector(wrapper, "_safeProjector").Order);
        Assert.Equal("CU", GetPrivateProjector(wrapper, "_unsafeProjector").Order);
        Assert.Equal(1, accessor.SafeVersion);
        Assert.Equal(2, accessor.UnsafeVersion);

        var consumed = wrapper.GetUnsafeProjection(fixture.DomainTypes);

        Assert.Equal("ABCU", consumed.State.Order);
        Assert.Equal(10, consumed.State.Total);
        Assert.Equal(4, consumed.Version);
        Assert.Equal(6, fixture.TagTypes.FoldCount); // rebuild A/B/C once, then reconcile buffered U once
        Assert.Equal(6, fixture.TagTypes.TagResolutionCount);

        _ = wrapper.GetUnsafeProjection(fixture.DomainTypes);
        Assert.Equal(6, fixture.TagTypes.FoldCount);
        Assert.Equal(6, fixture.TagTypes.TagResolutionCount);
    }

    [Fact]
    public void DeferredRepair_IsLosslessForSortableUniqueIdTies()
    {
        var fixture = new Fixture();
        var baseline = DateTime.UtcNow.AddMinutes(-10);
        var tiedPosition = SortableUniqueId.Generate(baseline, Guid.Empty);
        var laterPosition = SortableUniqueId.Generate(baseline.AddTicks(1), Guid.Empty);
        var firstTie = CreateEvent(new FoldEvent(1, "A"), tiedPosition, Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var secondTie = CreateEvent(new FoldEvent(2, "B"), tiedPosition, Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var later = CreateEvent(new FoldEvent(4, "C"), laterPosition, Guid.Parse("00000000-0000-0000-0000-000000000003"));

        var immediate = fixture.CreateWrapper();
        immediate.ProcessEvent(firstTie, SortableUniqueId.MaxValue, fixture.DomainTypes);
        _ = immediate.GetUnsafeProjection(fixture.DomainTypes);
        immediate.ProcessEvent(secondTie, SortableUniqueId.MaxValue, fixture.DomainTypes);
        _ = immediate.GetUnsafeProjection(fixture.DomainTypes);
        immediate.ProcessEvent(later, SortableUniqueId.MaxValue, fixture.DomainTypes);
        var immediateProjection = immediate.GetUnsafeProjection(fixture.DomainTypes);

        var deferredFixture = new Fixture();
        var deferred = deferredFixture.CreateWrapper();
        deferred.ProcessEvent(later, SortableUniqueId.MaxValue, deferredFixture.DomainTypes);
        deferred.ProcessEvent(secondTie, SortableUniqueId.MaxValue, deferredFixture.DomainTypes);
        deferred.ProcessEvent(firstTie, SortableUniqueId.MaxValue, deferredFixture.DomainTypes);
        var deferredProjection = deferred.GetUnsafeProjection(deferredFixture.DomainTypes);

        Assert.Equal("ABC", immediateProjection.State.Order);
        Assert.Equal(immediateProjection.State, deferredProjection.State);
        Assert.Equal(immediateProjection.LastEventId, deferredProjection.LastEventId);
        Assert.Equal(immediateProjection.LastSortableUniqueId, deferredProjection.LastSortableUniqueId);
        Assert.Equal(3, deferredProjection.Version);
    }

    [Fact]
    public void DeferredRepair_FoldAndTagResolutionCountsAreLinearAndEachDirtyEpochRepairsOnce()
    {
        const int smallCount = 64;
        var small = RunDeferredComplexityScenario(smallCount);
        var large = RunDeferredComplexityScenario(smallCount * 2);

        // Deterministic fold counts are the primary complexity proof. The only insertion-time fold is the first high
        // arrival; the first consume then folds N retained events once. Doubling N with one consume remains linear.
        Assert.Equal(smallCount + 1, small.FoldsAfterFirstConsume);
        Assert.Equal((smallCount * 2) + 1, large.FoldsAfterFirstConsume);
        Assert.Equal(small.FoldsAfterFirstConsume, small.TagResolutionsAfterFirstConsume);
        Assert.Equal(large.FoldsAfterFirstConsume, large.TagResolutionsAfterFirstConsume);
        Assert.Equal(2, small.FoldStartCountAfterFirstConsume); // initial in-order fold + one deferred rebuild
        Assert.Equal(2, large.FoldStartCountAfterFirstConsume);

        // A repeated consume is free, while a later out-of-order epoch adds exactly one further complete rebuild.
        Assert.Equal(small.FoldsAfterFirstConsume, small.FoldsAfterRepeatConsume);
        Assert.Equal(3, small.FoldStartCountAfterSecondEpoch);
        Assert.Equal((smallCount * 2) + 4, small.FoldsAfterSecondEpoch);
    }

    [Fact]
    public void RepairFailure_PublishesNothingRetainsCauseAndBlocksCompactionUntilRetry()
    {
        var fixture = new Fixture();
        var wrapper = fixture.CreateWrapper();
        var seed = CreateSeedEvents();
        var accessor = (IDualStateAccessor)wrapper;

        wrapper.ProcessEvent(seed.High, SortableUniqueId.MaxValue, fixture.DomainTypes);
        wrapper.ProcessEvent(seed.Low, SortableUniqueId.MaxValue, fixture.DomainTypes);
        fixture.TagTypes.FailDuringFold.Add(seed.High.Id);

        // This snapshot includes both payloads and every published safe/unsafe metadata field, so repair cannot leak a
        // partial checkpoint before its failing high replay event is reported.
        var published = CapturePublishedState(wrapper, accessor);

        var failure = Assert.Throws<InvalidOperationException>(() => wrapper.GetUnsafeProjection(fixture.DomainTypes));
        Assert.Equal(seed.High.Id.ToString(), failure.Data["DeferredSafeRepairEventId"]);
        Assert.Equal(seed.High.SortableUniqueIdValue, failure.Data["DeferredSafeRepairPosition"]);
        Assert.Equal(seed.Low.Id.ToString(), failure.Data["DeferredSafeRepairDirtyEventId"]);
        Assert.Equal(seed.Low.SortableUniqueIdValue, failure.Data["DeferredSafeRepairDirtyPosition"]);
        AssertPublishedState(published, wrapper, accessor);
        Assert.True(GetPrivateBool(wrapper, "_safeHistoryDirty"));
        Assert.True(GetPrivateBool(wrapper, "_servedStateDirty"));
        Assert.Equal(2, GetPrivateCount(wrapper, "_allSafeEvents"));

        Assert.Throws<InvalidOperationException>(() => accessor.CompactSafeHistory());
        Assert.False(GetPrivateBool(wrapper, "_useIncrementalSafePromotion"));
        Assert.Equal(2, GetPrivateCount(wrapper, "_allSafeEvents"));

        fixture.TagTypes.FailDuringFold.Remove(seed.High.Id);
        accessor.CompactSafeHistory();

        var repaired = Assert.IsType<CountingProjector>(accessor.GetSafeProjectorPayload());
        Assert.Equal(4, repaired.Total);
        Assert.Equal("AC", repaired.Order);
        Assert.Equal(0, GetPrivateCount(wrapper, "_allSafeEvents"));
        Assert.True(GetPrivateBool(wrapper, "_useIncrementalSafePromotion"));
    }

    [Fact]
    public async Task RepairFailure_FailsClosedAtTheProductionSnapshotPersistenceBoundary()
    {
        var fixture = new Fixture();
        var logger = new RecordingLogger();
        var actor = new GeneralMultiProjectionActor(
            fixture.DomainTypes,
            CountingProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1 },
            logger);
        var seed = CreateSeedEvents();

        await actor.AddEventsAsync([seed.High]);
        await actor.AddEventsAsync([seed.Low]);
        var wrapper = Assert.IsType<DualStateProjectionWrapper<CountingProjector>>(
            GetPrivateValue<object>(actor, "_singleStateAccessor"));
        var accessor = (IDualStateAccessor)wrapper;
        var published = CapturePublishedState(wrapper, accessor);
        var original = new InvalidOperationException($"Configured snapshot replay failure for {seed.High.Id}");
        fixture.TagTypes.ReplayFailures[seed.High.Id] = original;

        var result = await actor.BuildSnapshotForPersistenceAsync();

        Assert.False(result.IsSuccess);
        var failure = Assert.IsType<InvalidOperationException>(result.GetException());
        Assert.Same(original, failure);
        Assert.Equal(original.Message, failure.Message);
        Assert.Contains("RebuildSafeState", failure.StackTrace, StringComparison.Ordinal);
        Assert.Equal(seed.High.Id.ToString(), failure.Data["DeferredSafeRepairEventId"]);
        Assert.Equal(seed.High.SortableUniqueIdValue, failure.Data["DeferredSafeRepairPosition"]);
        Assert.Equal(seed.Low.Id.ToString(), failure.Data["DeferredSafeRepairDirtyEventId"]);
        Assert.Equal(seed.Low.SortableUniqueIdValue, failure.Data["DeferredSafeRepairDirtyPosition"]);
        var fault = Assert.IsType<ProjectionFaultDescriptor>(actor.CurrentFault);
        Assert.Equal(seed.High.Id, fault.EventId);
        Assert.Equal(seed.High.SortableUniqueIdValue, fault.Position);
        var firstObserved = Assert.Single(
            logger.Entries,
            entry => entry.EventId.Name == "ProjectionFaultFirstObserved");
        Assert.Same(original, firstObserved.Exception);
        AssertPublishedState(published, wrapper, accessor);
        Assert.True(GetPrivateBool(wrapper, "_safeHistoryDirty"));
        Assert.True(GetPrivateBool(wrapper, "_servedStateDirty"));
        Assert.Equal(2, GetPrivateCount(wrapper, "_allSafeEvents"));
        Assert.Equal(3, fixture.TagTypes.FoldCount); // low folded into an unpublished local replay, then high failed

        fixture.TagTypes.ReplayFailures.Remove(seed.High.Id);
        actor.ClearFaultForRebuild();
        var retried = await actor.BuildSnapshotForPersistenceAsync();
        Assert.True(retried.IsSuccess);
        Assert.Equal(seed.High.SortableUniqueIdValue, retried.GetValue().SafeLastSortableUniqueId);
    }

    [Fact]
    public void PublicDualStateSurface_IsFrozenWithoutDeferredRepairMembers()
    {
        var wrapperPublicMethods = typeof(DualStateProjectionWrapper<CountingProjector>)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["GetSafeProjection", "GetUnsafeProjection", "ProcessEvent"], wrapperPublicMethods);

        var accessorMethods = typeof(IDualStateAccessor)
            .GetMethods()
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                "CompactSafeHistory",
                "GetSafeProjectorPayload",
                "GetUnsafeProjectorPayload",
                "ProcessEventAs",
                "PromoteBufferedEvents"
            ],
            accessorMethods);
        Assert.DoesNotContain(accessorMethods, name => name.Contains("Consume", StringComparison.Ordinal));
    }

    private static ComplexityObservation RunDeferredComplexityScenario(int eventCount)
    {
        var fixture = new Fixture();
        var wrapper = fixture.CreateWrapper();
        var baseline = DateTime.UtcNow.AddDays(-1);
        var events = Enumerable.Range(0, eventCount)
            .Select(index => CreateEvent(
                new FoldEvent(1, index.ToString("D4")),
                SortableUniqueId.Generate(baseline.AddTicks(index), Guid.Empty),
                GuidFromInt(index + 1)))
            .ToArray();

        foreach (var ev in events.Reverse())
        {
            wrapper.ProcessEvent(ev, SortableUniqueId.MaxValue, fixture.DomainTypes);
        }

        Assert.Equal(1, fixture.TagTypes.FoldCount);
        Assert.Equal(1, fixture.TagTypes.TagResolutionCount);

        _ = wrapper.GetUnsafeProjection(fixture.DomainTypes);
        var foldsAfterFirstConsume = fixture.TagTypes.FoldCount;
        var tagsAfterFirstConsume = fixture.TagTypes.TagResolutionCount;
        var startsAfterFirstConsume = fixture.TagTypes.FoldStartCount;

        _ = wrapper.GetUnsafeProjection(fixture.DomainTypes);
        var foldsAfterRepeatConsume = fixture.TagTypes.FoldCount;

        var later = CreateEvent(
            new FoldEvent(1, "later"),
            SortableUniqueId.Generate(baseline.AddTicks(eventCount + 2), Guid.Empty),
            GuidFromInt(eventCount + 100));
        var beforeLater = CreateEvent(
            new FoldEvent(1, "before-later"),
            SortableUniqueId.Generate(baseline.AddTicks(eventCount + 1), Guid.Empty),
            GuidFromInt(eventCount + 101));
        wrapper.ProcessEvent(later, SortableUniqueId.MaxValue, fixture.DomainTypes);
        wrapper.ProcessEvent(beforeLater, SortableUniqueId.MaxValue, fixture.DomainTypes);
        _ = wrapper.GetUnsafeProjection(fixture.DomainTypes);

        return new ComplexityObservation(
            foldsAfterFirstConsume,
            tagsAfterFirstConsume,
            startsAfterFirstConsume,
            foldsAfterRepeatConsume,
            fixture.TagTypes.FoldCount,
            fixture.TagTypes.FoldStartCount);
    }

    private static void AssertConsumedState(
        DualStateProjectionWrapper<CountingProjector> wrapper,
        IDualStateAccessor accessor,
        Fixture fixture,
        DirtySeed seed,
        int expectedFoldCount)
    {
        var safe = GetPrivateProjector(wrapper, "_safeProjector");
        var unsafeState = GetPrivateProjector(wrapper, "_unsafeProjector");
        Assert.Equal(6, safe.Total);
        Assert.Equal("ABC", safe.Order);
        Assert.Equal(safe, unsafeState);
        Assert.Equal(3, accessor.SafeVersion);
        Assert.Equal(3, accessor.UnsafeVersion);
        Assert.Equal(seed.High.Id, GetPrivateValue<Guid>(wrapper, "_safeLastEventId"));
        Assert.Equal(seed.High.SortableUniqueIdValue, GetPrivateValue<string>(wrapper, "_safeLastSortableUniqueId"));
        Assert.Equal(seed.High.Id, accessor.UnsafeLastEventId);
        Assert.Equal(seed.High.SortableUniqueIdValue, accessor.UnsafeLastSortableUniqueId);
        Assert.Equal(expectedFoldCount, fixture.TagTypes.FoldCount);
        Assert.Equal(expectedFoldCount, fixture.TagTypes.TagResolutionCount);
    }

    private static DirtySeed SeedDirtyHistory(DualStateProjectionWrapper<CountingProjector> wrapper, DcbDomainTypes domainTypes)
    {
        var seed = CreateSeedEvents();
        wrapper.ProcessEvent(seed.High, SortableUniqueId.MaxValue, domainTypes);
        wrapper.ProcessEvent(seed.Low, SortableUniqueId.MaxValue, domainTypes);
        wrapper.ProcessEvent(seed.Middle, SortableUniqueId.MaxValue, domainTypes);
        return seed;
    }

    private static void InvokeWrapperEntryPoint(
        string entryPoint,
        DualStateProjectionWrapper<CountingProjector> wrapper,
        IDualStateAccessor accessor,
        DcbDomainTypes domainTypes)
    {
        switch (entryPoint)
        {
            case "GetSafeProjection":
                _ = wrapper.GetSafeProjection(SortableUniqueId.MaxValue, domainTypes);
                return;
            case "GetUnsafeProjection":
                _ = wrapper.GetUnsafeProjection(domainTypes);
                return;
            case "PromoteBufferedEventsZero":
                // No event crosses the threshold here. This must nevertheless consume the existing dirty safe history.
                accessor.PromoteBufferedEvents(SortableUniqueId.MaxValue, domainTypes);
                return;
            case "GetSafeProjectorPayload":
                _ = accessor.GetSafeProjectorPayload();
                return;
            case "GetUnsafeProjectorPayload":
                _ = accessor.GetUnsafeProjectorPayload();
                return;
            case "CompactSafeHistory":
                accessor.CompactSafeHistory();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(entryPoint), entryPoint, null);
        }
    }

    private static PublishedState CapturePublishedState(
        DualStateProjectionWrapper<CountingProjector> wrapper,
        IDualStateAccessor accessor) =>
        new(
            GetPrivateProjector(wrapper, "_safeProjector"),
            accessor.SafeVersion,
            GetPrivateValue<Guid>(wrapper, "_safeLastEventId"),
            GetPrivateValue<string>(wrapper, "_safeLastSortableUniqueId"),
            GetPrivateProjector(wrapper, "_unsafeProjector"),
            accessor.UnsafeVersion,
            accessor.UnsafeLastEventId,
            accessor.UnsafeLastSortableUniqueId);

    private static void AssertPublishedState(
        PublishedState expected,
        DualStateProjectionWrapper<CountingProjector> wrapper,
        IDualStateAccessor accessor)
    {
        Assert.Equal(expected.SafeProjector, GetPrivateProjector(wrapper, "_safeProjector"));
        Assert.Equal(expected.SafeVersion, accessor.SafeVersion);
        Assert.Equal(expected.SafeLastEventId, GetPrivateValue<Guid>(wrapper, "_safeLastEventId"));
        Assert.Equal(expected.SafeLastSortableUniqueId, GetPrivateValue<string>(wrapper, "_safeLastSortableUniqueId"));
        Assert.Equal(expected.UnsafeProjector, GetPrivateProjector(wrapper, "_unsafeProjector"));
        Assert.Equal(expected.UnsafeVersion, accessor.UnsafeVersion);
        Assert.Equal(expected.UnsafeLastEventId, accessor.UnsafeLastEventId);
        Assert.Equal(expected.UnsafeLastSortableUniqueId, accessor.UnsafeLastSortableUniqueId);
    }

    private static DirtySeed CreateSeedEvents()
    {
        var baseline = DateTime.UtcNow.AddMinutes(-10);
        return new DirtySeed(
            CreateEvent(new FoldEvent(1, "A"), SortableUniqueId.Generate(baseline, Guid.Empty), Guid.Parse("00000000-0000-0000-0000-000000000011")),
            CreateEvent(new FoldEvent(2, "B"), SortableUniqueId.Generate(baseline.AddTicks(1), Guid.Empty), Guid.Parse("00000000-0000-0000-0000-000000000012")),
            CreateEvent(new FoldEvent(3, "C"), SortableUniqueId.Generate(baseline.AddTicks(2), Guid.Empty), Guid.Parse("00000000-0000-0000-0000-000000000013")));
    }

    private static Event CreateEvent(FoldEvent payload, string sortableUniqueId, Guid eventId) =>
        new(
            payload,
            sortableUniqueId,
            nameof(FoldEvent),
            eventId,
            new EventMetadata(eventId.ToString(), eventId.ToString(), "test"),
            ["count:all"]);

    private static Guid GuidFromInt(int value) => new($"00000000-0000-0000-0000-{value:D12}");

    private static int GetPrivateCount(object target, string fieldName)
    {
        var value = GetPrivateValue<object>(target, fieldName);
        var count = value.GetType().GetProperty("Count")?.GetValue(value);
        return Assert.IsType<int>(count);
    }

    private static bool GetPrivateBool(object target, string fieldName) => GetPrivateValue<bool>(target, fieldName);

    private static CountingProjector GetPrivateProjector(object target, string fieldName) =>
        Assert.IsType<CountingProjector>(GetPrivateValue<object>(target, fieldName));

    private static T GetPrivateValue<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsAssignableFrom<T>(field!.GetValue(target));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            var eventTypes = new SimpleEventTypes();
            eventTypes.RegisterEventType<FoldEvent>(nameof(FoldEvent));
            TagTypes = new CountingTagTypes();
            var multiProjectorTypes = new SimpleMultiProjectorTypes();
            multiProjectorTypes.RegisterProjector<CountingProjector>();
            DomainTypes = new DcbDomainTypes(
                eventTypes,
                TagTypes,
                new SimpleTagProjectorTypes(),
                new SimpleTagStatePayloadTypes(),
                multiProjectorTypes,
                new SimpleQueryTypes(),
                new System.Text.Json.JsonSerializerOptions());
            Types = multiProjectorTypes;
        }

        public CountingTagTypes TagTypes { get; }
        public DcbDomainTypes DomainTypes { get; }
        public SimpleMultiProjectorTypes Types { get; }

        public DualStateProjectionWrapper<CountingProjector> CreateWrapper() => new(
            CountingProjector.GenerateInitialPayload(),
            CountingProjector.MultiProjectorName,
            Types,
            DomainTypes.JsonSerializerOptions);
    }

    private sealed class CountingTagTypes : ITagTypes
    {
        public int TagResolutionCount { get; set; }
        public int FoldCount { get; set; }
        public int FoldStartCount { get; set; }
        public HashSet<Guid> FailDuringFold { get; } = [];
        public Dictionary<Guid, Exception> ReplayFailures { get; } = [];

        public ITag GetTag(string tag)
        {
            TagResolutionCount++;
            return new FallbackTag("count", tag);
        }

        public IReadOnlyList<string> GetAllTagGroupNames() => Array.Empty<string>();
    }

    private sealed record DirtySeed(Event Low, Event Middle, Event High);

    private sealed record PublishedState(
        CountingProjector SafeProjector,
        int SafeVersion,
        Guid SafeLastEventId,
        string SafeLastSortableUniqueId,
        CountingProjector UnsafeProjector,
        int UnsafeVersion,
        Guid UnsafeLastEventId,
        string UnsafeLastSortableUniqueId);

    private sealed record ComplexityObservation(
        int FoldsAfterFirstConsume,
        int TagResolutionsAfterFirstConsume,
        int FoldStartCountAfterFirstConsume,
        int FoldsAfterRepeatConsume,
        int FoldsAfterSecondEpoch,
        int FoldStartCountAfterSecondEpoch);

    public sealed record FoldEvent(int Amount, string Label) : IEventPayload;

    public sealed record CountingProjector(int Total = 0, string Order = "") : IMultiProjector<CountingProjector>
    {
        public CountingProjector() : this(0, string.Empty) { }

        public static string MultiProjectorName => nameof(CountingProjector);
        public static string MultiProjectorVersion => "1.0.0";
        public static CountingProjector GenerateInitialPayload() => new();

        public static ResultBox<CountingProjector> Project(
            CountingProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold)
        {
            var counters = Assert.IsType<CountingTagTypes>(domainTypes.TagTypes);
            counters.FoldCount++;
            if (payload.Order.Length == 0)
            {
                counters.FoldStartCount++;
            }

            if (counters.ReplayFailures.TryGetValue(ev.Id, out var replayFailure))
            {
                return ResultBox.Error<CountingProjector>(replayFailure);
            }

            if (counters.FailDuringFold.Contains(ev.Id))
            {
                return ResultBox.Error<CountingProjector>(
                    new InvalidOperationException($"Configured fold failure for {ev.Id}"));
            }

            return ev.Payload is FoldEvent fold
                ? ResultBox.FromValue(payload with { Total = payload.Total + fold.Amount, Order = payload.Order + fold.Label })
                : ResultBox.FromValue(payload);
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopDisposable.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(eventId, exception, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(EventId EventId, Exception? Exception, string Message);

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}
