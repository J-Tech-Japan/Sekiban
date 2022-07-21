using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public class MultipleAggregateProjectionService : IMultipleAggregateProjectionService
{
    private readonly IDocumentRepository _documentRepository;

    public MultipleAggregateProjectionService(IDocumentRepository documentRepository) =>
        _documentRepository = documentRepository;

    public Task<P> GetProjectionAsync<P>() where P : MultipleAggregateProjectionBase<P>, IMultipleAggregateProjectionDto, new() =>
        GetMultipleProjectionAsync<P, P>();

    public Task<SingleAggregateProjectionDto<AggregateDtoBase<TContents>>> GetAggregateListObject<T, TContents>()
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents =>
        GetMultipleProjectionAsync<SingleAggregateListProjector<T, AggregateDtoBase<TContents>, DefaultSingleAggregateProjector<T>>,
            SingleAggregateProjectionDto<AggregateDtoBase<TContents>>>();
    public async Task<List<AggregateDtoBase<TContents>>> GetAggregateList<T, TContents>(QueryListType queryListType = QueryListType.ActiveOnly)
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents
    {
        var list = (await GetMultipleProjectionAsync<SingleAggregateListProjector<T, AggregateDtoBase<TContents>, DefaultSingleAggregateProjector<T>>,
            SingleAggregateProjectionDto<AggregateDtoBase<TContents>>>()).List;
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => list,
            QueryListType.ActiveOnly => list.Where(m => m.IsDeleted == false).ToList(),
            QueryListType.DeletedOnly => list.Where(m => m.IsDeleted).ToList(),
            _ => list.Where(m => m.IsDeleted == false).ToList()
        };
    }
    public Task<SingleAggregateProjectionDto<T>> GetSingleAggregateProjectionListObject<T>() where T : SingleAggregateProjectionBase<T>, new() =>
        GetMultipleProjectionAsync<SingleAggregateListProjector<T, T, T>, SingleAggregateProjectionDto<T>>();
    public async Task<List<T>> GetSingleAggregateProjectionList<T>(QueryListType queryListType = QueryListType.ActiveOnly)
        where T : SingleAggregateProjectionBase<T>, new()
    {
        var list = (await GetMultipleProjectionAsync<SingleAggregateListProjector<T, T, T>, SingleAggregateProjectionDto<T>>()).List;
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => list,
            QueryListType.ActiveOnly => list.Where(m => m.IsDeleted == false).ToList(),
            QueryListType.DeletedOnly => list.Where(m => m.IsDeleted).ToList(),
            _ => list.Where(m => m.IsDeleted == false).ToList()
        };
    }

    private async Task<Q> GetMultipleProjectionAsync<P, Q>() where P : IMultipleAggregateProjector<Q>, new() where Q : IMultipleAggregateProjectionDto
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
