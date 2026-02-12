using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Runtime.Native;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class NativeTagStateProjectionPrimitiveTests
{
    [Fact]
    public async Task ProjectAsync_ShouldBuildSerializableState_FromSerializedEvents()
    {
        var primitive = BuildPrimitive();
        var events = BuildSerializedEvents(1, 2);
        var request = new TagStateProjectionRequest(
            TagStateId.Parse("Counter:sample:CounterProjector"),
            events.Last().SortableUniqueIdValue,
            CachedState: null,
            Events: events);

        var result = await primitive.ProjectAsync(request);

        Assert.True(result.IsSuccess);
        var state = result.GetValue();
        Assert.Equal(2, state.Version);
        Assert.Equal("Counter", state.TagGroup);
        Assert.Equal("sample", state.TagContent);
        Assert.Equal("CounterProjector", state.TagProjector);
        Assert.Equal(nameof(CounterState), state.TagPayloadName);
        Assert.Equal(events.Last().SortableUniqueIdValue, state.LastSortedUniqueId);
    }

    [Fact]
    public async Task ProjectAsync_ShouldApplyIncrementalUpdate_WhenCachedStateProvided()
    {
        var primitive = BuildPrimitive();
        var allEvents = BuildSerializedEvents(1, 2);
        var firstEvents = allEvents.Take(1).ToList();
        var firstRequest = new TagStateProjectionRequest(
            TagStateId.Parse("Counter:sample:CounterProjector"),
            firstEvents.Last().SortableUniqueIdValue,
            CachedState: null,
            Events: firstEvents);
        var firstResult = await primitive.ProjectAsync(firstRequest);
        Assert.True(firstResult.IsSuccess);

        var secondRequest = new TagStateProjectionRequest(
            TagStateId.Parse("Counter:sample:CounterProjector"),
            allEvents.Last().SortableUniqueIdValue,
            CachedState: firstResult.GetValue(),
            Events: allEvents);

        var secondResult = await primitive.ProjectAsync(secondRequest);

        Assert.True(secondResult.IsSuccess);
        var state = secondResult.GetValue();
        Assert.Equal(2, state.Version);
        Assert.Equal(allEvents.Last().SortableUniqueIdValue, state.LastSortedUniqueId);
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
