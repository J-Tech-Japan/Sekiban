namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public class MultipleAggregateProjectionService
{
    private readonly IDocumentRepository _documentRepository;

    public MultipleAggregateProjectionService(IDocumentRepository documentRepository) =>
        _documentRepository = documentRepository;

    private async Task<Q> GetMultipleProjectionAsync<P, Q>() where P : IMultipleAggregateProjector<Q>, new() where Q : IMultipleAggregateProjectionDto
    {
        var projector = new P();
        await _documentRepository.GetAllAggregateEventsAsync(
            typeof(P),
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
