using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Query.MultiProjections.Projections;

/// <summary>
///     Simple Multi Projection
/// </summary>
public class SimpleMultiProjection(EventRepository documentRepository, RegisteredEventTypes registeredEventTypes)
    : IMultiProjection
{

    public async Task<MultiProjectionState<TProjectionPayload>>
        GetMultiProjectionAsync<TProjection, TProjectionPayload>(
            string rootPartitionKey,
            MultiProjectionRetrievalOptions? retrievalOptions)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon
    {
        var projector = new TProjection();
        await documentRepository.GetEvents(
            EventRetrievalInfo.FromNullableValues(
                rootPartitionKey,
                new MultiProjectionTypeStream(typeof(TProjection), projector.TargetAggregateNames()),
                null,
                ISortableIdCondition.None),
            events =>
            {
                foreach (var ev in events)
                {
                    projector.ApplyEvent(ev);
                }
            });
        return projector.ToState();
    }
    public async Task<MultiProjectionState<TProjectionPayload>>
        GetInitialMultiProjectionFromStreamAsync<TProjection, TProjectionPayload>(
            Stream stream,
            string rootPartitionKey,
            MultiProjectionRetrievalOptions? retrievalOptions)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon
    {
        await Task.CompletedTask;
        var list = JsonSerializer.Deserialize<List<JsonElement>>(stream) ??
            throw new SekibanSerializerException("Could not deserialize file");
        var events = (IList<IEvent>)list
            .Select(m => SekibanJsonHelper.DeserializeToEvent(m, registeredEventTypes.RegisteredTypes))
            .Where(m => m is not null)
            .ToList();
        return events.Aggregate(
            new MultiProjectionState<TProjectionPayload>(),
            (projection, ev) => projection.ApplyEvent(ev));
    }
    public async Task<MultiProjectionState<TProjectionPayload>>
        GetMultiProjectionFromMultipleStreamAsync<TProjection, TProjectionPayload>(
            Func<Task<Stream?>> stream,
            string rootPartitionKey,
            MultiProjectionRetrievalOptions? retrievalOptions)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon
    {
        var eventStream = await stream();
        var multiProjectionState = new MultiProjectionState<TProjectionPayload>();

        while (eventStream != null)
        {
            var list = JsonSerializer.Deserialize<List<JsonElement>>(eventStream) ??
                throw new SekibanSerializerException("Could not deserialize file");
            var events = (IList<IEvent>)list
                .Select(m => SekibanJsonHelper.DeserializeToEvent(m, registeredEventTypes.RegisteredTypes))
                .Where(m => m is not null)
                .ToList();
            multiProjectionState = events.Aggregate(
                multiProjectionState,
                (projection, ev) => projection.ApplyEvent(ev));
            eventStream = await stream();
        }
        return multiProjectionState;
    }
}
