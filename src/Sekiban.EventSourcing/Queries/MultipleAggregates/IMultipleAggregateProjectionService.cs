using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public interface IMultipleAggregateProjectionService
{

    public Task<MultipleAggregateProjectionContentsDto<TContents>> GetProjectionAsync<TProjection, TContents>()
        where TProjection : MultipleAggregateProjectionBase<TContents>, new() where TContents : IMultipleAggregateProjectionContents, new();
    public Task<MultipleAggregateProjectionContentsDto<SingleAggregateProjectionDto<AggregateDto<TContents>>>>
        GetAggregateListObject<TAggregate, TContents>() where TAggregate : TransferableAggregateBase<TContents>
        where TContents : IAggregateContents, new();
    public Task<List<AggregateDto<TContents>>> GetAggregateList<TAggregate, TContents>(QueryListType queryListType = QueryListType.ActiveOnly)
        where TAggregate : TransferableAggregateBase<TContents> where TContents : IAggregateContents, new();
    public Task<MultipleAggregateProjectionContentsDto<SingleAggregateProjectionDto<TSingleAggregateProjection>>>
        GetSingleAggregateProjectionListObject<TAggregate, TSingleAggregateProjection>() where TAggregate : AggregateBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection>, new();
    public Task<List<TSingleAggregateProjection>> GetSingleAggregateProjectionList<TAggregate, TSingleAggregateProjection>(
        QueryListType queryListType = QueryListType.ActiveOnly) where TAggregate : AggregateBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection>, new();
}
