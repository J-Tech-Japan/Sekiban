using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Query.MultiProjections.Projections;

/// <summary>
///     Simple Multi Projection
/// </summary>
public class SimpleMultiProjection : IMultiProjection
{
    private readonly IDocumentRepository _documentRepository;
    private readonly RegisteredEventTypes _registeredEventTypes;
    public SimpleMultiProjection(IDocumentRepository documentRepository, RegisteredEventTypes registeredEventTypes)
    {
        _documentRepository = documentRepository;
        _registeredEventTypes = registeredEventTypes;
    }

    public async Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjection, TProjectionPayload>(
        string rootPartitionKey,
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var projector = new TProjection();
        await _documentRepository.GetAllEventsAsync(
            typeof(TProjection),
            projector.TargetAggregateNames(),
            null,
            rootPartitionKey,
            events =>
            {
                foreach (var ev in events)
                {
                    projector.ApplyEvent(ev);
                }
            });
        return projector.ToState();
    }
    public async Task<MultiProjectionState<TProjectionPayload>> GetInitialMultiProjectionFromStreamAsync<TProjection, TProjectionPayload>(
        Stream stream,
        string rootPartitionKey,
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        await Task.CompletedTask;
        var list = JsonSerializer.Deserialize<List<JsonElement>>(stream) ?? throw new Exception("Could not deserialize file");
        var events = (IList<IEvent>)list.Select(m => SekibanJsonHelper.DeserializeToEvent(m, _registeredEventTypes.RegisteredTypes))
            .Where(m => m is not null)
            .ToList();
        return events.Aggregate(new MultiProjectionState<TProjectionPayload>(), (projection, ev) => projection.ApplyEvent(ev));
    }
    public async Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionFromMultipleStreamAsync<TProjection, TProjectionPayload>(
        Func<Task<Stream?>> stream,
        string rootPartitionKey,
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var eventStream = await stream();
        var multiProjectionState = new MultiProjectionState<TProjectionPayload>();

        while (eventStream != null)
        {
            var list = JsonSerializer.Deserialize<List<JsonElement>>(eventStream) ?? throw new Exception("Could not deserialize file");
            var events = (IList<IEvent>)list.Select(m => SekibanJsonHelper.DeserializeToEvent(m, _registeredEventTypes.RegisteredTypes))
                .Where(m => m is not null)
                .ToList();
            multiProjectionState = events.Aggregate(multiProjectionState, (projection, ev) => projection.ApplyEvent(ev));
            eventStream = await stream();
        }
        return multiProjectionState;
    }
}
