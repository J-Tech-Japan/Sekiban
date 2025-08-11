using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Orleans.Parts;
namespace Sekiban.Pure.Orleans.Grains;

public class AggregateEventHandlerGrain(
    [PersistentState("aggregate", "Default")] IPersistentState<AggregateEventHandlerGrainToPersist> state,
    SekibanDomainTypes sekibanDomainTypes,
    IEventWriter eventWriter,
    IEventReader eventReader) : Grain, IAggregateEventHandlerGrain
{
    private readonly List<IEvent> _events = new();

    public async Task<IReadOnlyList<IEvent>> AppendEventsAsync(
        string expectedLastSortableUniqueId,
        IReadOnlyList<IEvent> newEvents)
    {
        var streamProvider = this.GetStreamProvider("EventStreamProvider");
        var toStoreEvents = newEvents.ToList().ToEventsAndReplaceTime(sekibanDomainTypes.EventTypes);
        var currentLast = state.State?.LastSortableUniqueId ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(expectedLastSortableUniqueId) && currentLast != expectedLastSortableUniqueId)
            throw new InvalidCastException("Expected last event ID does not match");
        if (!string.IsNullOrWhiteSpace(currentLast) &&
            toStoreEvents.Any() &&
            string.Compare(
                currentLast,
                toStoreEvents.First().SortableUniqueId,
                StringComparison.Ordinal) > 0)
            throw new InvalidCastException("Expected last event ID is later than new events");

        if (toStoreEvents.Any())
        {
            var persist = AggregateEventHandlerGrainToPersist.FromEvents(toStoreEvents);
            state.State = persist;
            await state.WriteStateAsync();
        }
        _events.AddRange(toStoreEvents);
        var orleansEvents = toStoreEvents;
        if (toStoreEvents.Count != 0) await eventWriter.SaveEvents(toStoreEvents);
        var stream = streamProvider.GetStream<IEvent>(StreamId.Create("AllEvents", Guid.Empty));
        foreach (var ev in orleansEvents) await stream.OnNextAsync(ev);
        return await Task.FromResult(orleansEvents);
    }


    public Task<IReadOnlyList<IEvent>> GetDeltaEventsAsync(string fromSortableUniqueId, int? limit = null)
    {
        var index = _events.FindIndex(e => e.SortableUniqueId == fromSortableUniqueId);
        if (index < 0)
        {
            return Task.FromResult<IReadOnlyList<IEvent>>(new List<IEvent>());
        }
        return Task.FromResult<IReadOnlyList<IEvent>>(_events.Skip(index + 1).Take(limit ?? int.MaxValue).ToList());
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
        try
        {
            await state.ReadStateAsync();
        }
        catch
        {
            state.State = new AggregateEventHandlerGrainToPersist() { LastSortableUniqueId = string.Empty, LastEventDate = OptionalValue<DateTime>.Empty };
        }
        if (state.State == null)
        {
            state.State = new AggregateEventHandlerGrainToPersist() { LastSortableUniqueId = string.Empty, LastEventDate = OptionalValue<DateTime>.Empty };
        }
        if (!string.IsNullOrWhiteSpace(state.State.LastSortableUniqueId))
        {
            var retrievalInfo = PartitionKeys
                .FromPrimaryKeysString(this.GetPrimaryKeyString())
                .Remap(EventRetrievalInfo.FromPartitionKeys)
                .UnwrapBox();
            var events = await eventReader.GetEvents(retrievalInfo).UnwrapBox();
            _events.Clear();
            _events.AddRange(events);
        }
    }

}