using Sekiban.Core.Document;
namespace Sekiban.Core.Query.MultipleProjections.Projections;

public class SimpleMultipleProjection : IMultipleProjection
{
    private readonly IDocumentRepository _documentRepository;

    public SimpleMultipleProjection(IDocumentRepository documentRepository) => _documentRepository = documentRepository;

    public async Task<MultiProjectionState<TProjectionPayload>> GetMultipleProjectionAsync<TProjection, TProjectionPayload>()
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
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
