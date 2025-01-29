using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Types;

namespace Sekiban.Pure.OrleansEventSourcing;

public class AggregateEventHandlerGrain(
    [PersistentState(stateName: "aggregate", storageName: "Default")] IPersistentState<AggregateEventHandlerGrain.ToPersist> state,
    SekibanTypeConverters typeConverters) : Grain, IAggregateEventHandlerGrain
{
    public record ToPersist(string LastSortableUniqueId, OptionalValue<DateTime> LastUpdatedAt)
    {
        public static ToPersist FromEvents(IEnumerable<IEvent> events)
        {
            var lastEvent = events.LastOrDefault();
            var last = lastEvent?.SortableUniqueId ?? string.Empty;
            var value = new SortableUniqueIdValue(last);
            if (string.IsNullOrWhiteSpace(last))
            {
                return new ToPersist(string.Empty, OptionalValue<DateTime>.Empty);
            }
            return new ToPersist(
                last,
                value.GetTicks()
            );
        }
    }
    
    private List<IEvent> _events = new();
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await state.ReadStateAsync();
        if (state.State == null || string.IsNullOrWhiteSpace(state.State.LastSortableUniqueId))
        {
            state.State = new ToPersist(string.Empty, OptionalValue<DateTime>.Empty);
        }
    }

    public async Task<IReadOnlyList<OrleansEvent>> AppendEventsAsync(
        string expectedLastSortableUniqueId,
        IReadOnlyList<OrleansEvent> newEvents
    )
    {
        var streamProvider = this.GetStreamProvider("EventStreamProvider");
        var toStoreEvents = newEvents.ToList().ToEventsAndReplaceTime(typeConverters.EventTypes);
        if (string.IsNullOrWhiteSpace(expectedLastSortableUniqueId) && 
            _events.Count > 0 &&
            _events.Last().SortableUniqueId != expectedLastSortableUniqueId)
        {
            throw new InvalidCastException("Expected last event ID does not match");
        }
        // if last sortable unique id is not empty and it is later than newEvents, throw exception
        if (_events.Any() &&
            toStoreEvents.Any() &&
            String.Compare(_events.Last().SortableUniqueId, toStoreEvents.First().SortableUniqueId, StringComparison.Ordinal) > 0)
        {
            throw new InvalidCastException("Expected last event ID is later than new events");
        }

        var persist = ToPersist.FromEvents(toStoreEvents);
        state.State = persist;
        await state.WriteStateAsync();
        _events.AddRange(toStoreEvents);
        var orleansEvents = toStoreEvents.ToOrleansEvents();
        var stream = streamProvider.GetStream<OrleansEvent>(StreamId.Create("AllEvents", Guid.Empty));
        foreach (var ev in orleansEvents)
        {
            await stream.OnNextAsync(ev);
        }
        return await Task.FromResult(orleansEvents);
    }


    public Task<IReadOnlyList<OrleansEvent>> GetDeltaEventsAsync(
        string fromSortableUniqueId,
        int? limit = null
    )
    {
        var index = _events.FindIndex(e => e.SortableUniqueId == fromSortableUniqueId);
        
        if (index < 0)
            return Task.FromResult((IReadOnlyList<OrleansEvent>)new IEvent[0]);

        var events = _events.Skip(index + 1)
                            .Take(limit ?? int.MaxValue)
                            .ToList();

        return Task.FromResult((IReadOnlyList<OrleansEvent>)events);
    }

    public Task<IReadOnlyList<OrleansEvent>> GetAllEventsAsync()
    {
        return Task.FromResult((IReadOnlyList<OrleansEvent>)_events.ToList());
    }

    public Task<string> GetLastSortableUniqueIdAsync()
    {
        return Task.FromResult(state.State.LastSortableUniqueId);
    }

    public Task RegisterProjectorAsync(string projectorKey)
    {
        // No-op for in-memory implementation
        return Task.CompletedTask;
    }
}
