using Sekiban.EventSourcing.Queries.MultipleAggregates.MultipleProjection;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public class MultipleAggregateProjectionService : IMultipleAggregateProjectionService
{
    private readonly IMultipleProjection _multipleProjection;

    public MultipleAggregateProjectionService(IMultipleProjection multipleProjection)
    {
        _multipleProjection = multipleProjection;
    }
    public Task<P> GetProjectionAsync<P>() where P : MultipleAggregateProjectionBase<P>, IMultipleAggregateProjectionDto, new()
    {
        return _multipleProjection.GetMultipleProjectionAsync<P, P>();
    }

    public Task<SingleAggregateProjectionDto<AggregateDto<TContents>>> GetAggregateListObject<T, TContents>()
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents, new()
    {
        return _multipleProjection
            .GetMultipleProjectionAsync<SingleAggregateListProjector<T, AggregateDto<TContents>, DefaultSingleAggregateProjector<T>>,
                SingleAggregateProjectionDto<AggregateDto<TContents>>>();
    }
    public async Task<List<AggregateDto<TContents>>> GetAggregateList<T, TContents>(QueryListType queryListType = QueryListType.ActiveOnly)
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents, new()
    {
        var list = (await _multipleProjection
            .GetMultipleProjectionAsync<SingleAggregateListProjector<T, AggregateDto<TContents>, DefaultSingleAggregateProjector<T>>,
                SingleAggregateProjectionDto<AggregateDto<TContents>>>()).List;
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => list,
            QueryListType.ActiveOnly => list.Where(m => m.IsDeleted == false).ToList(),
            QueryListType.DeletedOnly => list.Where(m => m.IsDeleted).ToList(),
            _ => list.Where(m => m.IsDeleted == false).ToList()
        };
    }
    public Task<SingleAggregateProjectionDto<TSingleAggregateProjection>>
        GetSingleAggregateProjectionListObject<TAggregate, TSingleAggregateProjection>() where TAggregate : AggregateBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection>, new()
    {
        return _multipleProjection
            .GetMultipleProjectionAsync<
                SingleAggregateListProjector<TSingleAggregateProjection, TSingleAggregateProjection, TSingleAggregateProjection>,
                SingleAggregateProjectionDto<TSingleAggregateProjection>>();
    }
    public async Task<List<T>> GetSingleAggregateProjectionList<TAggregate, T>(QueryListType queryListType = QueryListType.ActiveOnly)
        where TAggregate : AggregateBase, new() where T : SingleAggregateProjectionBase<TAggregate, T>, new()
    {
        var list = (await _multipleProjection.GetMultipleProjectionAsync<SingleAggregateListProjector<T, T, T>, SingleAggregateProjectionDto<T>>())
            .List;
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => list,
            QueryListType.ActiveOnly => list.Where(m => m.IsDeleted == false).ToList(),
            QueryListType.DeletedOnly => list.Where(m => m.IsDeleted).ToList(),
            _ => list.Where(m => m.IsDeleted == false).ToList()
        };
    }
}
