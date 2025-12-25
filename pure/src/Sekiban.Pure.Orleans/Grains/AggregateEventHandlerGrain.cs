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

        // 楽観的並行性制御: 期待される最後のIDと現在の最後のIDが一致するかチェック
        if (!string.IsNullOrWhiteSpace(expectedLastSortableUniqueId) && currentLast != expectedLastSortableUniqueId)
            throw new InvalidCastException("Expected last event ID does not match");

        // 順序チェック: 現在の最後のイベントより前のタイムスタンプのイベントは受け入れない
        if (!string.IsNullOrWhiteSpace(currentLast) &&
            toStoreEvents.Any() &&
            string.Compare(currentLast, toStoreEvents.First().SortableUniqueId, StringComparison.Ordinal) > 0)
            throw new InvalidCastException("Expected last event ID is later than new events");

        if (toStoreEvents.Any())
        {
            // 全イベント（既存 + 新規）から最新の状態を作成
            _events.AddRange(toStoreEvents);
            var allEvents = _events.ToList();
            var persist = AggregateEventHandlerGrainToPersist.FromEvents(allEvents);
            state.State = persist;
            await state.WriteStateAsync();
        }

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

    public Task<string> GetLastSortableUniqueIdAsync() =>
        Task.FromResult(state.State?.LastSortableUniqueId ?? string.Empty);

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
            state.State = new AggregateEventHandlerGrainToPersist
                { LastSortableUniqueId = string.Empty, LastEventDate = OptionalValue<DateTime>.Empty };
        }
        if (state.State == null)
        {
            state.State = new AggregateEventHandlerGrainToPersist
                { LastSortableUniqueId = string.Empty, LastEventDate = OptionalValue<DateTime>.Empty };
        }
        // 永続化された state から最後のイベントID情報を取得
        var persistedLastId = state.State.LastSortableUniqueId;

        // 実際のイベントストアから全イベントを取得
        var retrievalInfo = PartitionKeys
            .FromPrimaryKeysString(this.GetPrimaryKeyString())
            .Remap(EventRetrievalInfo.FromPartitionKeys)
            .UnwrapBox();
        var events = await eventReader.GetEvents(retrievalInfo).UnwrapBox();
        _events.Clear();
        _events.AddRange(events);

        // 実際のイベントの最後のIDと永続化されたIDが異なる場合は state を更新
        var actualLastId = events.LastOrDefault()?.SortableUniqueId ?? string.Empty;
        if (actualLastId != persistedLastId)
        {
            var persist = AggregateEventHandlerGrainToPersist.FromEvents(events);
            state.State = persist;
            await state.WriteStateAsync();
        }
    }
}
