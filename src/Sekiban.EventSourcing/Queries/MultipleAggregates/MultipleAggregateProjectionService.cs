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

    public Task<MultipleAggregateProjectionContentsDto<TContents>> GetProjectionAsync<TProjection, TContents>()
        where TProjection : MultipleAggregateProjectionBase<TContents>, new() where TContents : IMultipleAggregateProjectionContents, new()
    {
        return _multipleProjection.GetMultipleProjectionAsync<TProjection, TContents>();
    }
    public async Task<List<AggregateDto<TContents>>> GetAggregateList<TAggregate, TContents>(QueryListType queryListType = QueryListType.ActiveOnly)
        where TAggregate : AggregateBase<TContents> where TContents : IAggregateContents, new()
    {
        var projection = await GetAggregateListObject<TAggregate, TContents>();
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => projection.Contents.List.ToList(),
            QueryListType.ActiveOnly => projection.Contents.List.Where(m => m.IsDeleted == false).ToList(),
            QueryListType.DeletedOnly => projection.Contents.List.Where(m => m.IsDeleted).ToList(),
            _ => projection.Contents.List.Where(m => m.IsDeleted == false).ToList()
        };
    }
    public
        Task<MultipleAggregateProjectionContentsDto<
            SingleAggregateListProjectionDto<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>>
        GetSingleAggregateProjectionListObject<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>()
        where TAggregate : AggregateCommonBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
    {
        return _multipleProjection
            .GetMultipleProjectionAsync<
                SingleAggregateListProjector<TSingleAggregateProjection, SingleAggregateProjectionDto<TSingleAggregateProjectionContents>,
                    TSingleAggregateProjection>,
                SingleAggregateListProjectionDto<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>();
    }
    public async Task<List<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>
        GetSingleAggregateProjectionList<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>(
            QueryListType queryListType = QueryListType.ActiveOnly) where TAggregate : AggregateCommonBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
    {
        var projection = await GetSingleAggregateProjectionListObject<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>();
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => projection.Contents.List.ToList(),
            QueryListType.ActiveOnly => projection.Contents.List.Where(m => m.IsDeleted == false).ToList(),
            QueryListType.DeletedOnly => projection.Contents.List.Where(m => m.IsDeleted).ToList(),
            _ => projection.Contents.List.Where(m => m.IsDeleted == false).ToList()
        };
    }
    public async Task<MultipleAggregateProjectionContentsDto<SingleAggregateListProjectionDto<AggregateDto<TContents>>>>
        GetAggregateListObject<TAggregate, TContents>() where TAggregate : AggregateBase<TContents>
        where TContents : IAggregateContents, new()
    {
        var list = await _multipleProjection
            .GetMultipleProjectionAsync<SingleAggregateListProjector<TAggregate, AggregateDto<TContents>, DefaultSingleAggregateProjector<TAggregate>>
                , SingleAggregateListProjectionDto<AggregateDto<TContents>>>();
        return list;
    }
}
