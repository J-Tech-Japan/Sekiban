namespace Sekiban.EventSourcing.Queries.MultipleAggregates.MultipleProjection;

public class SimpleMultipleProjection : IMultipleProjection
{
    private readonly IDocumentRepository _documentRepository;

    public SimpleMultipleProjection(IDocumentRepository documentRepository)
    {
        _documentRepository = documentRepository;
    }

    public async Task<MultipleAggregateProjectionContentsDto<TProjectionContents>> GetMultipleProjectionAsync<TProjection, TProjectionContents>()
        where TProjection : IMultipleAggregateProjector<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new()
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
        return projector.ToDto();
    }
}
