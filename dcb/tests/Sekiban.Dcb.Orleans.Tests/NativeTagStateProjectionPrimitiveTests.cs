using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Runtime.Native;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class NativeTagStateProjectionPrimitiveTests
{
    [Fact]
    public void ApplyEvents_ShouldBuildSerializableState_FromScratch()
    {
        var primitive = BuildPrimitive();
        var events = BuildSerializedEvents(1, 2);
        var tagStateId = TagStateId.Parse("Counter:sample:CounterProjector");
        var accumulator = primitive.CreateAccumulator(tagStateId);

        var appliedState = accumulator.ApplyState(null);
        var appliedEvents = accumulator.ApplyEvents(events, events.Last().SortableUniqueIdValue);
        var state = accumulator.GetSerializedState();

        Assert.True(appliedState);
        Assert.True(appliedEvents);
        Assert.Equal(2, state.Version);
        Assert.Equal("Counter", state.TagGroup);
        Assert.Equal("sample", state.TagContent);
        Assert.Equal("CounterProjector", state.TagProjector);
        Assert.Equal(nameof(CounterState), state.TagPayloadName);
        Assert.Equal(events.Last().SortableUniqueIdValue, state.LastSortedUniqueId);
    }

    [Fact]
    public void ApplyEvents_ShouldApplyIncrementalUpdate_WhenCachedStateProvided()
    {
        var primitive = BuildPrimitive();
        var allEvents = BuildSerializedEvents(1, 2);
        var firstEvents = allEvents.Take(1).ToList();
        var tagStateId = TagStateId.Parse("Counter:sample:CounterProjector");

        var firstState = ProjectState(primitive, tagStateId, firstEvents.Last().SortableUniqueIdValue, firstEvents, null);

        var accumulator = primitive.CreateAccumulator(tagStateId);
        Assert.True(accumulator.ApplyState(firstState));
        Assert.True(accumulator.ApplyEvents(allEvents, allEvents.Last().SortableUniqueIdValue));

        var finalState = accumulator.GetSerializedState();
        Assert.Equal(2, finalState.Version);
        Assert.Equal(allEvents.Last().SortableUniqueIdValue, finalState.LastSortedUniqueId);
    }

    [Fact]
    public void ApplyEvents_ShouldReuseCachedState_WhenNoNewEvents()
    {
        var primitive = BuildPrimitive();
        var events = BuildSerializedEvents(1);
        var tagStateId = TagStateId.Parse("Counter:sample:CounterProjector");
        var cached = ProjectState(primitive, tagStateId, events.Last().SortableUniqueIdValue, events, null);

        var accumulator = primitive.CreateAccumulator(tagStateId);
        Assert.True(accumulator.ApplyState(cached));
        Assert.True(accumulator.ApplyEvents(Array.Empty<SerializableEvent>(), events.Last().SortableUniqueIdValue));

        var finalState = accumulator.GetSerializedState();
        Assert.Same(cached, finalState);
        Assert.Equal(cached.Version, finalState.Version);
        Assert.Equal(cached.LastSortedUniqueId, finalState.LastSortedUniqueId);
    }

    private static NativeTagStateProjectionPrimitive BuildPrimitive()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<CounterIncremented>();

        var projectorTypes = new SimpleTagProjectorTypes();
        projectorTypes.RegisterProjector<CounterProjector>();

        var payloadTypes = new SimpleTagStatePayloadTypes();
        payloadTypes.RegisterPayloadType<CounterState>();

        return new NativeTagStateProjectionPrimitive(eventTypes, projectorTypes, payloadTypes);
    }

    private static SerializableTagState ProjectState(
        NativeTagStateProjectionPrimitive primitive,
        TagStateId tagStateId,
        string? latestSortableUniqueId,
        IReadOnlyList<SerializableEvent> events,
        SerializableTagState? cachedState)
    {
        var accumulator = primitive.CreateAccumulator(tagStateId);
        Assert.True(accumulator.ApplyState(cachedState));
        Assert.True(accumulator.ApplyEvents(events, latestSortableUniqueId));
        return accumulator.GetSerializedState();
    }

    private static List<SerializableEvent> BuildSerializedEvents(params int[] increments)
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<CounterIncremented>();
        var events = new List<SerializableEvent>();
        foreach (var increment in increments)
        {
            var sortable = SortableUniqueId.GenerateNew();
            var ev = new Event(
                new CounterIncremented(increment),
                sortable,
                nameof(CounterIncremented),
                Guid.CreateVersion7(),
                new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
                new List<string> { "Counter:sample" });
            events.Add(ev.ToSerializableEvent(eventTypes));
        }

        return events;
    }

    public record CounterIncremented(int Delta) : IEventPayload;

    public record CounterState(int Value) : ITagStatePayload;

    public class CounterProjector : ITagProjector<CounterProjector>
    {
        public static string ProjectorVersion => "v1";

        public static string ProjectorName => "CounterProjector";

        public static ITagStatePayload Project(ITagStatePayload current, Event ev)
        {
            var counter = current as CounterState ?? new CounterState(0);
            return ev.Payload is CounterIncremented incremented
                ? counter with { Value = counter.Value + incremented.Delta }
                : counter;
        }
    }
}
