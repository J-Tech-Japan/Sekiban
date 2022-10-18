using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query.MultipleAggregate;

public interface IMultipleAggregateProjectionService
{

    public Task<MultipleAggregateProjectionContentsDto<TContents>> GetProjectionAsync<TProjection, TContents>()
        where TProjection : MultipleAggregateProjectionBase<TContents>, new() where TContents : IMultipleAggregateProjectionContents, new();
    public Task<MultipleAggregateProjectionContentsDto<SingleAggregateListProjectionDto<AggregateDto<TContents>>>>
        GetAggregateListObject<TAggregate, TContents>() where TAggregate : AggregateBase<TContents> where TContents : IAggregateContents, new();
    public Task<List<AggregateDto<TContents>>> GetAggregateList<TAggregate, TContents>(QueryListType queryListType = QueryListType.ActiveOnly)
        where TAggregate : AggregateBase<TContents> where TContents : IAggregateContents, new();
    public
        Task<MultipleAggregateProjectionContentsDto<
            SingleAggregateListProjectionDto<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>>
        GetSingleAggregateProjectionListObject<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>()
        where TAggregate : AggregateCommonBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents;
    public Task<List<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>
        GetSingleAggregateProjectionList<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>(
            QueryListType queryListType = QueryListType.ActiveOnly) where TAggregate : AggregateCommonBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents;
}
