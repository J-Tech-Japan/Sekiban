using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     SEK-G18 (#1092) order-sensitive convergence: with a FIRST-EVENT-WINS projector (the globally-earliest
///     SortableUniqueId must win, later duplicates are no-ops), the served (unsafe) state must converge to the
///     globally-earliest event regardless of ARRIVAL order, and IsSafeState must be truthful (true only once the served
///     state is reconciled identical to the safe state). Commutative projectors cannot catch this class of bug, so these
///     use a deliberately order-sensitive fold.
/// </summary>
public class SafeWindowConvergenceTests
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly GeneralMultiProjectionActorOptions _options;

    public SafeWindowConvergenceTests()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<CreatedWithId>("CreatedWithId");

        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<FirstWinsProjector>();

        _domainTypes = new DcbDomainTypes(
            eventTypes,
            new SimpleTagTypes(),
            new SimpleTagProjectorTypes(),
            new SimpleTagStatePayloadTypes(),
            multiProjectorTypes,
            new SimpleQueryTypes());

        _options = new GeneralMultiProjectionActorOptions { SafeWindowMs = 5000 };
    }

    [Fact]
    public async Task LaterEventArrivesFirst_BothInWindow_ServedConvergesToEarliest_And_IsSafeStateTruthful()
    {
        var actor = new GeneralMultiProjectionActor(_domainTypes, FirstWinsProjector.MultiProjectorName, _options);

        // Two creates for the SAME id, both inside the safe window. "A" is globally EARLIER by SortableUniqueId.
        var earlier = CreateEvent(new CreatedWithId("team-1", "A"), DateTime.UtcNow.AddSeconds(-2.0));
        var later = CreateEvent(new CreatedWithId("team-1", "B"), DateTime.UtcNow.AddSeconds(-1.0));

        // Arrival order is REVERSED: the later (by SortableUniqueId) event arrives first.
        await actor.AddEventsAsync(new[] { later });
        await actor.AddEventsAsync(new[] { earlier });

        // Served/unsafe state must reconcile to the globally-earliest winner ("A"), not the first-arrived ("B").
        var served = await actor.GetStateAsync();
        var servedPayload = (FirstWinsProjector)served.GetValue().Payload;
        Assert.Equal("A", servedPayload.Winners["team-1"]);

        // Still buffered (inside window) ⇒ IsSafeState must be FALSE (not a timestamp-only lie).
        Assert.False(served.GetValue().IsSafeState);

        // After the window passes and both graduate, the safe state is the same globally-earliest winner, and the served
        // state (reconciled, empty buffer) is now truthfully safe.
        await Task.Delay(5500);
        await actor.AddEventsAsync(new[]
        {
            CreateEvent(new CreatedWithId("trigger", "t"), DateTime.UtcNow.AddSeconds(-10))
        });

        var safe = await actor.GetStateAsync(canGetUnsafeState: false);
        Assert.Equal("A", ((FirstWinsProjector)safe.GetValue().Payload).Winners["team-1"]);

        var servedAfter = await actor.GetStateAsync();
        Assert.Equal("A", ((FirstWinsProjector)servedAfter.GetValue().Payload).Winners["team-1"]);
        Assert.True(servedAfter.GetValue().IsSafeState);
    }

    [Fact]
    public async Task EarlierEventArrivesInLaterBatch_AfterGraduation_FreshPath_ConvergesLocally()
    {
        var actor = new GeneralMultiProjectionActor(_domainTypes, FirstWinsProjector.MultiProjectorName, _options);

        // The LATER event is already outside the window and graduates to safe first (locally-originated winner).
        var later = CreateEvent(new CreatedWithId("team-2", "B"), DateTime.UtcNow.AddSeconds(-8));
        await actor.AddEventsAsync(new[] { later });
        Assert.Equal("B", ((FirstWinsProjector)(await actor.GetStateAsync(canGetUnsafeState: false)).GetValue().Payload).Winners["team-2"]);

        // The globally-EARLIER event arrives later (cross-silo catch-up), already outside the window and OUT of global
        // order versus the safe head. In the fresh (non-compacted) path the wrapper re-sorts the retained history locally.
        var earlier = CreateEvent(new CreatedWithId("team-2", "A"), DateTime.UtcNow.AddSeconds(-9));
        await actor.AddEventsAsync(new[] { earlier });

        var safe = await actor.GetStateAsync(canGetUnsafeState: false);
        Assert.Equal("A", ((FirstWinsProjector)safe.GetValue().Payload).Winners["team-2"]); // converged to earliest
    }

    [Fact]
    public async Task ConvergenceEquivalence_ArbitraryInterleavings_EqualFromScratchGlobalReplay()
    {
        var baseTime = DateTime.UtcNow.AddSeconds(-30);
        var events = new List<Event>();
        // Multiple ids, each with a create; a few duplicate creates for the same id with later values.
        for (var i = 0; i < 6; i++)
        {
            events.Add(CreateEvent(new CreatedWithId($"id-{i}", $"first-{i}"), baseTime.AddSeconds(i)));
        }
        events.Add(CreateEvent(new CreatedWithId("id-1", "dup-later"), baseTime.AddSeconds(10)));
        events.Add(CreateEvent(new CreatedWithId("id-3", "dup-later"), baseTime.AddSeconds(11)));

        // From-scratch global-order replay (the authoritative expected result).
        var expected = FirstWinsProjector.GenerateInitialPayload();
        foreach (var ev in events.OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal))
        {
            expected = FirstWinsProjector.Project(expected, ev, new List<ITag>(), _domainTypes, new SortableUniqueId("0")).GetValue();
        }

        // Feed the SAME events to an actor in a scrambled order (deterministic scramble), one per batch.
        var actor = new GeneralMultiProjectionActor(_domainTypes, FirstWinsProjector.MultiProjectorName, _options);
        foreach (var ev in Scramble(events))
        {
            await actor.AddEventsAsync(new[] { ev });
        }
        // Force everything safe.
        await Task.Delay(5500);
        await actor.AddEventsAsync(new[] { CreateEvent(new CreatedWithId("flush", "f"), baseTime) });

        var safe = (FirstWinsProjector)(await actor.GetStateAsync(canGetUnsafeState: false)).GetValue().Payload;
        foreach (var id in expected.Winners.Keys)
        {
            Assert.Equal(expected.Winners[id], safe.Winners[id]);
        }
    }

    [Fact]
    public void IncrementalPath_OutOfOrderSafeArrival_SignalsRebuildRequired()
    {
        // A restored (incremental / compacted-baseline) wrapper cannot reorder locally, so an out-of-global-order
        // already-safe arrival must set RebuildRequired for the grain/host to replay from the authoritative store.
        var restored = FirstWinsProjector.GenerateInitialPayload();
        var accessor = (IDualStateAccessor)DualStateProjectionWrapperFactory.CreateFromRestoredSnapshot(
            restored,
            FirstWinsProjector.MultiProjectorName,
            _domainTypes.MultiProjectorTypes,
            _domainTypes,
            new SortableUniqueId("000000000000000000000000000000000000000000000000").Value,
            initialVersion: 0)!;

        var threshold = new SortableUniqueId(SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-5), Guid.Empty));

        // First a later (by SortableUniqueId) already-safe event graduates the safe head.
        accessor.ProcessEventAs(CreateEvent(new CreatedWithId("t", "B"), DateTime.UtcNow.AddSeconds(-8)), threshold, _domainTypes);
        Assert.False(ReadRebuildRequired(accessor));

        // Then a globally-EARLIER already-safe event arrives out of order — incremental path cannot reorder it.
        accessor.ProcessEventAs(CreateEvent(new CreatedWithId("t", "A"), DateTime.UtcNow.AddSeconds(-9)), threshold, _domainTypes);
        Assert.True(ReadRebuildRequired(accessor));
    }

    // The rebuild signal is an INTERNAL seam (IDualStateRebuildSignals) — no public API. Read it via reflection for the
    // wrapper-level unit assertion; the grain-level observable behavior is covered by the Orleans rebuild tests.
    private static bool ReadRebuildRequired(object wrapper)
    {
        var seam = typeof(IDualStateAccessor).Assembly.GetType("Sekiban.Dcb.MultiProjections.IDualStateRebuildSignals")!;
        return (bool)seam.GetProperty("RebuildRequired")!.GetValue(wrapper)!;
    }

    [Fact]
    public async Task TwoIndependentInstances_SharedEvents_DifferentArrivalOrder_ConvergeToGloballyEarliest()
    {
        // Two independent projection instances (the closest in-repo equivalent of two independent clusters over a shared
        // store) each receive the SAME two racing create events for the same id, but in DIFFERENT arrival order. After the
        // events become safe, both must converge to the globally-earliest (by SortableUniqueId) winner — independent of
        // SEK-G19's reservation fix (both duplicate creates are permitted to land).
        var earlier = CreateEvent(new CreatedWithId("team-shared", "A"), DateTime.UtcNow.AddSeconds(-9));
        var later = CreateEvent(new CreatedWithId("team-shared", "B"), DateTime.UtcNow.AddSeconds(-8));

        var instanceA = new GeneralMultiProjectionActor(_domainTypes, FirstWinsProjector.MultiProjectorName, _options);
        var instanceB = new GeneralMultiProjectionActor(_domainTypes, FirstWinsProjector.MultiProjectorName, _options);

        // Instance A sees its locally-originated (later) event first, then the cross-instance (earlier) event.
        await instanceA.AddEventsAsync(new[] { later });
        await instanceA.AddEventsAsync(new[] { earlier });

        // Instance B sees them in the opposite order.
        await instanceB.AddEventsAsync(new[] { earlier });
        await instanceB.AddEventsAsync(new[] { later });

        var safeA = (FirstWinsProjector)(await instanceA.GetStateAsync(canGetUnsafeState: false)).GetValue().Payload;
        var safeB = (FirstWinsProjector)(await instanceB.GetStateAsync(canGetUnsafeState: false)).GetValue().Payload;

        Assert.Equal("A", safeA.Winners["team-shared"]);
        Assert.Equal("A", safeB.Winners["team-shared"]); // both converged to the globally-earliest event
        Assert.Equal(safeA.Winners["team-shared"], safeB.Winners["team-shared"]);
    }

    private static IEnumerable<Event> Scramble(List<Event> events)
    {
        // Deterministic non-sorted order: interleave from both ends.
        var result = new List<Event>();
        int lo = 0, hi = events.Count - 1;
        while (lo <= hi)
        {
            result.Add(events[hi]);
            if (lo != hi) result.Add(events[lo]);
            lo++;
            hi--;
        }
        return result;
    }

    private static Event CreateEvent(IEventPayload payload, DateTime timestamp)
    {
        var sortableId = SortableUniqueId.Generate(timestamp, Guid.NewGuid());
        return new Event(
            payload,
            sortableId,
            payload.GetType().Name,
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestUser"),
            new List<string>());
    }

    public record CreatedWithId(string Id, string Value) : IEventPayload;

    /// <summary>First-event-wins: the first create (by fold order) for an id wins; later duplicates are no-ops.</summary>
    public record FirstWinsProjector : IMultiProjector<FirstWinsProjector>
    {
        public Dictionary<string, string> Winners { get; init; } = new();

        public static string MultiProjectorName => "FirstWinsProjector";
        public static string MultiProjectorVersion => "1.0.0";
        public static FirstWinsProjector GenerateInitialPayload() => new();

        public static ResultBox<FirstWinsProjector> Project(
            FirstWinsProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold)
        {
            if (ev.Payload is CreatedWithId created)
            {
                if (payload.Winners.ContainsKey(created.Id))
                {
                    return ResultBox.FromValue(payload); // first-event-wins: no-op for a duplicate id
                }
                var next = new Dictionary<string, string>(payload.Winners) { [created.Id] = created.Value };
                return ResultBox.FromValue(payload with { Winners = next });
            }
            return ResultBox.FromValue(payload);
        }
    }
}
