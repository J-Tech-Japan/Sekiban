using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates
{
    public interface IMultipleAggregateProjectionService
    {

        public Task<TProjection> GetProjectionAsync<TProjection>()
            where TProjection : MultipleAggregateProjectionBase<TProjection>, IMultipleAggregateProjectionDto, new();
        public Task<TProjection> GetListProjectionAsync<TProjection, TRecord>()
            where TProjection : MultipleAggregateListProjectionBase<TProjection, TRecord>, new() where TRecord : new();
        public Task<SingleAggregateProjectionDto<AggregateDto<TContents>>> GetAggregateListObject<TAggregate, TContents>()
            where TAggregate : TransferableAggregateBase<TContents> where TContents : IAggregateContents, new();
        public Task<List<AggregateDto<TContents>>> GetAggregateList<TAggregate, TContents>(QueryListType queryListType = QueryListType.ActiveOnly)
            where TAggregate : TransferableAggregateBase<TContents> where TContents : IAggregateContents, new();
        public Task<SingleAggregateProjectionDto<TSingleAggregateProjection>> GetSingleAggregateProjectionListObject<TSingleAggregateProjection>()
            where TSingleAggregateProjection : SingleAggregateProjectionBase<TSingleAggregateProjection>, new();
        public Task<List<T>> GetSingleAggregateProjectionList<T>(QueryListType queryListType = QueryListType.ActiveOnly)
            where T : SingleAggregateProjectionBase<T>, new();
    }
}
