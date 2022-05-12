using Sekiban.EventSourcing.Queries.SingleAggregates;
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

    public Task<SingleAggregateProjectionDto<Q>> GetAggregateList<T, Q>() where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase =>
        GetMultipleProjectionAsync<SingleAggregateListProjector<T, Q, DefaultSingleAggregateProjector<T>>, SingleAggregateProjectionDto<Q>>();
    public Task<SingleAggregateProjectionDto<T>> GetSingleAggregateProjectionList<T>() where T : SingleAggregateProjectionBase<T>, new() =>
        GetMultipleProjectionAsync<SingleAggregateListProjector<T, T, T>, SingleAggregateProjectionDto<T>>();
}
