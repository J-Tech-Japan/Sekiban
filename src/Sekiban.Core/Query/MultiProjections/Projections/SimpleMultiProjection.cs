using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Query.MultiProjections.Projections;

public class SimpleMultiProjection : IMultiProjection
{
    private readonly IDocumentRepository _documentRepository;

    public SimpleMultiProjection(IDocumentRepository documentRepository) => _documentRepository = documentRepository;

    public async Task<MultiProjectionState<TProjectionPayload>>
        GetMultiProjectionAsync<TProjection, TProjectionPayload>(SortableUniqueIdValue? includesSortableUniqueIdValue)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var projector = new TProjection();
        await _documentRepository.GetAllEventsAsync(
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
