using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultipleAggregate.MultipleProjection;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query.MultipleAggregate;

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
    public async Task<MultipleAggregateProjectionContentsDto<SingleAggregateListProjectionDto<AggregateState<TAggregatePayload>>>> GetAggregateListObject<TAggregatePayload>() where TAggregatePayload : IAggregatePayload, new()
    {
        var list = await _multipleProjection
            .GetMultipleProjectionAsync<SingleAggregateListProjector<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>, DefaultSingleAggregateProjector<TAggregatePayload>>
                , SingleAggregateListProjectionDto<AggregateState<TAggregatePayload>>>();
        return list;
    }
    public async Task<List<AggregateState<TAggregatePayload>>> GetAggregateList<TAggregatePayload>(QueryListType queryListType = QueryListType.ActiveOnly) where TAggregatePayload : IAggregatePayload, new()
    {
        var projection = await GetAggregateListObject<TAggregatePayload>();
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
        where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionPayload
    {
        return _multipleProjection
            .GetMultipleProjectionAsync<
                SingleAggregateListProjector<TSingleAggregateProjection, SingleAggregateProjectionDto<TSingleAggregateProjectionContents>,
                    TSingleAggregateProjection>,
                SingleAggregateListProjectionDto<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>();
    }
    public async Task<List<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>
        GetSingleAggregateProjectionList<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>(
            QueryListType queryListType = QueryListType.ActiveOnly) where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionPayload
    {
        var projection = await GetSingleAggregateProjectionListObject<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>();
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => projection.Contents.List.ToList(),
            QueryListType.ActiveOnly => projection.Contents.List.Where(m => m.GetIsDeleted() == false).ToList(),
            QueryListType.DeletedOnly => projection.Contents.List.Where(m => m.GetIsDeleted()).ToList(),
            _ => projection.Contents.List.Where(m => m.GetIsDeleted() == false).ToList()
        };
    }
}
