using Sekiban.Core.Document;
namespace Sekiban.Core.Query.MultipleAggregate.MultipleProjection;

public class SimpleMultipleProjection : IMultipleProjection
{
    private readonly IDocumentRepository _documentRepository;

    public SimpleMultipleProjection(IDocumentRepository documentRepository)
    {
        _documentRepository = documentRepository;
    }

    public async Task<MultipleAggregateProjectionState<TProjectionPayload>> GetMultipleProjectionAsync<TProjection, TProjectionPayload>()
        where TProjection : IMultipleAggregateProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
    {
        var projector = new TProjection();
        await _documentRepository.GetAllAggregateEventsAsync(
            typeof(TProjection),
            projector.TargetAggregateNames(),
            null,
            events =>
            {
                foreach (var ev in events)
                {
                    projector.ApplyEvent(ev);
                }
            });
        return projector.ToState();
    }
}
