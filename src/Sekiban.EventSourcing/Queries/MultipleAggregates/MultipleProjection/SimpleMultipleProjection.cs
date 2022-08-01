namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public class SimpleMultipleProjection : IMultipleProjection
{
    private readonly IDocumentRepository _documentRepository;

    public SimpleMultipleProjection(IDocumentRepository documentRepository) =>
        _documentRepository = documentRepository;

    public async Task<Q> GetMultipleProjectionAsync<P, Q>() where P : IMultipleAggregateProjector<Q>, new() where Q : IMultipleAggregateProjectionDto
    {
        var projector = new P();
        await _documentRepository.GetAllAggregateEventsAsync(
            typeof(P),
            projector.TargetAggregateNames(),
            null,
            events =>
            {
                foreach (var ev in events)
                {
                    projector.ApplyEvent(ev);
                }
            });
        return projector.ToDto();
    }
}
