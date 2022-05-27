using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public interface IMultipleAggregateProjectionService
{

    public Task<P> GetProjectionAsync<P>() where P : MultipleAggregateProjectionBase<P>, IMultipleAggregateProjectionDto, new();
    public Task<SingleAggregateProjectionDto<Q>> GetAggregateListObject<T, Q>() where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase;
    public Task<List<Q>> GetAggregateList<T, Q>(QueryListType queryListType = QueryListType.ActiveOnly)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase;
    public Task<SingleAggregateProjectionDto<T>> GetSingleAggregateProjectionListObject<T>() where T : SingleAggregateProjectionBase<T>, new();
    public Task<List<T>> GetSingleAggregateProjectionList<T>(QueryListType queryListType = QueryListType.ActiveOnly)
        where T : SingleAggregateProjectionBase<T>, new();
}
