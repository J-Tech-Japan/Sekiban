using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests;

public class GeneralMultiProjectionActorDynamicSafeWindowTests
{
    private readonly DcbDomainTypes _domainTypes;

    public GeneralMultiProjectionActorDynamicSafeWindowTests()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<TestEventCreated>("TestEventCreated");

        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();

        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<TestDynamicProjector>();

        _domainTypes = new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            new SimpleQueryTypes());
    }

    [Fact]
    public async Task DynamicSafeWindow_ExpandsByObservedLag_ForStreamEvents()
    {
        // Base safe window 5s, EMA=1 for determinism, no decay
        var options = new GeneralMultiProjectionActorOptions
        {
            SafeWindowMs = 5000,
            EnableDynamicSafeWindow = true,
            MaxExtraSafeWindowMs = 60000,
            LagEmaAlpha = 1.0,
            LagDecayPerSecond = 1.0
        };

        var actor = new GeneralMultiProjectionActor(_domainTypes, TestDynamicProjector.MultiProjectorName, options);

        // Create two events: 20s ago (safe even with dynamic), and 12s ago (unsafe if effective window=17s)
        var e20 = CreateEvent(new TestEventCreated("E20"), DateTime.UtcNow.AddSeconds(-20));
        var e12 = CreateEvent(new TestEventCreated("E12"), DateTime.UtcNow.AddSeconds(-12));

        await actor.AddEventsAsync(new[] { e20, e12 }, finishedCatchUp: true, source: EventSource.Stream);

        var safeState = await actor.GetStateAsync(canGetUnsafeState: false);
        var unsafeState = await actor.GetUnsafeStateAsync();

        Assert.True(safeState.IsSuccess);
        Assert.True(unsafeState.IsSuccess);

        var safePayload = (TestDynamicProjector)safeState.GetValue().Payload;
        var unsafePayload = (TestDynamicProjector)unsafeState.GetValue().Payload;

        // New behavior uses batch max lag: observed ~20s, effective window ~25s => both events are unsafe
        Assert.Empty(safePayload.Items);

        // Unsafe has both
        Assert.Equal(2, unsafePayload.Items.Count);
        Assert.Contains("E20", unsafePayload.Items);
        Assert.Contains("E12", unsafePayload.Items);
    }

    [Fact]
    public async Task DynamicSafeWindow_AlignsToSlowest_WithDecayedMax()
    {
        var options = new GeneralMultiProjectionActorOptions
        {
            SafeWindowMs = 5000,
            EnableDynamicSafeWindow = true,
            MaxExtraSafeWindowMs = 60000,
            LagEmaAlpha = 0.5,
            LagDecayPerSecond = 1.0
        };

        var actor = new GeneralMultiProjectionActor(_domainTypes, TestDynamicProjector.MultiProjectorName, options);

        // First batch: very slow (25s lag)
        var slow = CreateEvent(new TestEventCreated("SlowBatch"), DateTime.UtcNow.AddSeconds(-25));
        await actor.AddEventsAsync(new[] { slow }, finishedCatchUp: true, source: EventSource.Stream);

        // Second batch: fast (2s lag)
        var fast = CreateEvent(new TestEventCreated("FastBatch"), DateTime.UtcNow.AddSeconds(-2));
        await actor.AddEventsAsync(new[] { fast }, finishedCatchUp: true, source: EventSource.Stream);

        // Now send an event 15s ago; since decayed max is ~25s, effective window ~30s → this is unsafe
        var mid = CreateEvent(new TestEventCreated("Mid"), DateTime.UtcNow.AddSeconds(-15));
        await actor.AddEventsAsync(new[] { mid }, finishedCatchUp: true, source: EventSource.Stream);

        var safeState = await actor.GetStateAsync(canGetUnsafeState: false);
        var unsafeState = await actor.GetUnsafeStateAsync();

        Assert.True(safeState.IsSuccess);
        Assert.True(unsafeState.IsSuccess);

        var safePayload = (TestDynamicProjector)safeState.GetValue().Payload;
        var unsafePayload = (TestDynamicProjector)unsafeState.GetValue().Payload;

        // Safe should NOT include the 15s event yet
        Assert.DoesNotContain("Mid", safePayload.Items);
        // Unsafe includes all
        Assert.Contains("Mid", unsafePayload.Items);
    }

    private Event CreateEvent(IEventPayload payload, DateTime timestamp)
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

    public record TestEventCreated(string Name) : IEventPayload;

    public record TestDynamicProjector : IMultiProjector<TestDynamicProjector>
    {
        public List<string> Items { get; init; }
        public TestDynamicProjector() : this(new List<string>()) { }
        public TestDynamicProjector(List<string> items) => Items = items;
        public static TestDynamicProjector GenerateInitialPayload() => new(new List<string>());
        public static string MultiProjectorName => "TestDynamicProjector";
        public static string MultiProjectorVersion => "1.0.0";

        public static ResultBox<TestDynamicProjector> Project(TestDynamicProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold)
        {
            // Always append created items regardless of tags
            if (ev.Payload is TestEventCreated created)
            {
                return ResultBox.FromValue(new TestDynamicProjector(payload.Items.Concat(new[] { created.Name }).ToList()));
            }
            return ResultBox.FromValue(payload);
        }
    }
}
