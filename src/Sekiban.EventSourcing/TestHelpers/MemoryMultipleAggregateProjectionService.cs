using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.TestHelpers;

public class MemoryMultipleAggregateProjectionService : IMultipleAggregateProjectionService
{
    public List<dynamic> Objects { get; init; } = new();
    public async Task<MultipleAggregateProjectionContentsDto<TContents>> GetProjectionAsync<TProjection, TContents>()
        where TProjection : MultipleAggregateProjectionBase<TContents>, new() where TContents : IMultipleAggregateProjectionContents, new()
    {
        await Task.CompletedTask;
        return Objects.FirstOrDefault(m => m is MultipleAggregateProjectionContentsDto<TContents>) ?? throw new SekibanProjectionNotExistsException();
    }
    public async Task<MultipleAggregateProjectionContentsDto<SingleAggregateProjectionDto<AggregateDto<TContents>>>>
        GetAggregateListObject<TAggregate, TContents>() where TAggregate : TransferableAggregateBase<TContents>
        where TContents : IAggregateContents, new()
    {
        var aggregates = Objects.Where(m => m.Contents.GetType().Name == typeof(TContents).Name).Select(m => (AggregateDto<TContents>)m).ToList();
        await Task.CompletedTask;
        return new MultipleAggregateProjectionContentsDto<SingleAggregateProjectionDto<AggregateDto<TContents>>>(
            new SingleAggregateProjectionDto<AggregateDto<TContents>> { List = aggregates },
            Guid.Empty,
            string.Empty,
            0,
            aggregates.Count);
    }

    public async Task<List<AggregateDto<TContents>>> GetAggregateList<T, TContents>(QueryListType queryListType = QueryListType.ActiveOnly)
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents, new()
    {
        var aggregates = Objects.Where(m => m.Contents.GetType().Name == typeof(TContents).Name).Select(m => (AggregateDto<TContents>)m).ToList();
        await Task.CompletedTask;
        return aggregates;
    }
    public async Task<MultipleAggregateProjectionContentsDto<SingleAggregateProjectionDto<TSingleAggregateProjection>>>
        GetSingleAggregateProjectionListObject<TAggregate, TSingleAggregateProjection>() where TAggregate : AggregateBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection>, new()
    {
        var aggregates = Objects.Where(m => m.GetType().Name == typeof(TSingleAggregateProjection).Name)
            .Select(m => (TSingleAggregateProjection)m)
            .ToList();
        await Task.CompletedTask;
        return new MultipleAggregateProjectionContentsDto<SingleAggregateProjectionDto<TSingleAggregateProjection>>(
            new SingleAggregateProjectionDto<TSingleAggregateProjection> { List = aggregates },
            Guid.Empty,
            string.Empty,
            0,
            aggregates.Count);
    }
    public async Task<List<T>> GetSingleAggregateProjectionList<TAggregate, T>(QueryListType queryListType = QueryListType.ActiveOnly)
        where TAggregate : AggregateBase, new() where T : SingleAggregateProjectionBase<TAggregate, T>, new()
    {
        var aggregates = Objects.Where(m => m.GetType().Name == typeof(T).Name).Select(m => (T)m).ToList();
        await Task.CompletedTask;
        return aggregates;
    }
}
