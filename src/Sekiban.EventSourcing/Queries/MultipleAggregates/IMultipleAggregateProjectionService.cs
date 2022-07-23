using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public interface IMultipleAggregateProjectionService
{

    public Task<P> GetProjectionAsync<P>() where P : MultipleAggregateProjectionBase<P>, IMultipleAggregateProjectionDto, new();
    public Task<SingleAggregateProjectionDto<AggregateDto<TContents>>> GetAggregateListObject<T, TContents>()
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents, new();
    public Task<List<AggregateDto<TContents>>> GetAggregateList<T, TContents>(QueryListType queryListType = QueryListType.ActiveOnly)
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents, new();
    public Task<SingleAggregateProjectionDto<T>> GetSingleAggregateProjectionListObject<T>() where T : SingleAggregateProjectionBase<T>, new();
    public Task<List<T>> GetSingleAggregateProjectionList<T>(QueryListType queryListType = QueryListType.ActiveOnly)
        where T : SingleAggregateProjectionBase<T>, new();
}
