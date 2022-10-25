using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query.MultipleAggregate;

public interface IMultipleAggregateProjectionService
{

    public Task<MultipleAggregateProjectionContentsDto<TContents>> GetProjectionAsync<TProjection, TContents>()
        where TProjection : MultipleAggregateProjectionBase<TContents>, new() where TContents : IMultipleAggregateProjectionContents, new();
    public Task<MultipleAggregateProjectionContentsDto<SingleAggregateListProjectionDto<AggregateState<TAggregatePayload>>>>
        GetAggregateListObject<TAggregatePayload>() where TAggregatePayload : IAggregatePayload, new();
    public Task<List<AggregateState<TAggregatePayload>>> GetAggregateList<TAggregatePayload>(QueryListType queryListType = QueryListType.ActiveOnly) where TAggregatePayload : IAggregatePayload, new();
    public
        Task<MultipleAggregateProjectionContentsDto<
            SingleAggregateListProjectionDto<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>>
        GetSingleAggregateProjectionListObject<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>()
        where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionPayload;
    public Task<List<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>
        GetSingleAggregateProjectionList<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>(
            QueryListType queryListType = QueryListType.ActiveOnly) where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionPayload;
}
