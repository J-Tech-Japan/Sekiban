using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Orleans.Parts;
namespace Sekiban.Pure.Orleans.Grains;

public class AggregateEventHandlerGrain(
    [PersistentState("aggregate", "Default")]
    IPersistentState<AggregateEventHandlerGrain.ToPersist> state,
    SekibanDomainTypes sekibanDomainTypes,
    IEventWriter eventWriter,
    IEventReader eventReader) : Grain, IAggregateEventHandlerGrain
{
    private readonly List<IEvent> _events = new();

    public async Task<IReadOnlyList<IEvent>> AppendEventsAsync(
        string expectedLastSortableUniqueId,
        IReadOnlyList<IEvent> newEvents
    )
    {
        var streamProvider = this.GetStreamProvider("EventStreamProvider");
        var toStoreEvents = newEvents.ToList().ToEventsAndReplaceTime(sekibanDomainTypes.EventTypes);
        if (string.IsNullOrWhiteSpace(expectedLastSortableUniqueId) &&
            _events.Count > 0 &&
            _events.Last().SortableUniqueId != expectedLastSortableUniqueId)
            throw new InvalidCastException("Expected last event ID does not match");
        // if last sortable unique id is not empty and it is later than newEvents, throw exception
        if (_events.Any() &&
            toStoreEvents.Any() &&
            string.Compare(
                _events.Last().SortableUniqueId,
                toStoreEvents.First().SortableUniqueId,
                StringComparison.Ordinal) >
            0)
            throw new InvalidCastException("Expected last event ID is later than new events");

        var persist = ToPersist.FromEvents(toStoreEvents);
        state.State = persist;
        await state.WriteStateAsync();
        _events.AddRange(toStoreEvents);
        var orleansEvents = toStoreEvents;
        if (toStoreEvents.Count != 0) await eventWriter.SaveEvents(toStoreEvents);
        var stream = streamProvider.GetStream<IEvent>(StreamId.Create("AllEvents", Guid.Empty));
        foreach (var ev in orleansEvents) await stream.OnNextAsync(ev);
        return await Task.FromResult(orleansEvents);
    }


    public Task<IReadOnlyList<IEvent>> GetDeltaEventsAsync(
        string fromSortableUniqueId,
        int? limit = null
    )
    {
        var index = _events.FindIndex(e => e.SortableUniqueId == fromSortableUniqueId);

        if (index < 0)
            return Task.FromResult<IReadOnlyList<IEvent>>(new List<IEvent>());

        return Task.FromResult<IReadOnlyList<IEvent>>(
            _events
                .Skip(index + 1)
                .Take(limit ?? int.MaxValue)
                .ToList());
    }

    public async Task<IReadOnlyList<IEvent>> GetAllEventsAsync()
    {
        var retrievalInfo = PartitionKeys
            .FromPrimaryKeysString(this.GetPrimaryKeyString())
            .Remap(EventRetrievalInfo.FromPartitionKeys)
            .UnwrapBox();

        var events = await eventReader.GetEvents(retrievalInfo).UnwrapBox();
        return events.ToList();
    }

    public Task<string> GetLastSortableUniqueIdAsync() => Task.FromResult(state.State.LastSortableUniqueId);

    public Task RegisterProjectorAsync(string projectorKey) =>
        // No-op for in-memory implementation
        Task.CompletedTask;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await state.ReadStateAsync();
        if (state.State == null || string.IsNullOrWhiteSpace(state.State.LastSortableUniqueId))
            state.State = new ToPersist(string.Empty, OptionalValue<DateTime>.Empty);
    }

    public record ToPersist(string LastSortableUniqueId, OptionalValue<DateTime> LastUpdatedAt)
    {
        public static ToPersist FromEvents(IEnumerable<IEvent> events)
        {
            var lastEvent = events.LastOrDefault();
            var last = lastEvent?.SortableUniqueId ?? string.Empty;
            var value = new SortableUniqueIdValue(last);
            if (string.IsNullOrWhiteSpace(last)) return new ToPersist(string.Empty, OptionalValue<DateTime>.Empty);
            return new ToPersist(
                last,
                value.GetTicks()
            );
        }
    }
}