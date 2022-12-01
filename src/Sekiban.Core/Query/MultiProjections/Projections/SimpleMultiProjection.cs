using Sekiban.Core.Document;

namespace Sekiban.Core.Query.MultiProjections.Projections;

public class SimpleMultiProjection : IMultiProjection
{
    private readonly IDocumentRepository _documentRepository;

    public SimpleMultiProjection(IDocumentRepository documentRepository)
    {
        _documentRepository = documentRepository;
    }

    public async Task<MultiProjectionState<TProjectionPayload>>
        GetMultiProjectionAsync<TProjection, TProjectionPayload>()
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
    {
        var projector = new TProjection();
        await _documentRepository.GetAllEventsAsync(
            typeof(TProjection),
            projector.TargetAggregateNames(),
            null,
            events =>
            {
                foreach (var ev in events) projector.ApplyEvent(ev);
            });
        return projector.ToState();
    }
}
