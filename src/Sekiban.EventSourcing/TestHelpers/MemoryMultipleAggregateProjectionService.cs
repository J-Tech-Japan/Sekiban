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
    public async Task<MultipleAggregateProjectionContentsDto<SingleAggregateListProjectionDto<AggregateDto<TContents>>>>
        GetAggregateListObject<TAggregate, TContents>() where TAggregate : TransferableAggregateBase<TContents>
        where TContents : IAggregateContents, new()
    {
        var aggregates = Objects.Where(m => m.Contents.GetType().Name == typeof(TContents).Name).Select(m => (AggregateDto<TContents>)m).ToList();
        await Task.CompletedTask;
        return new MultipleAggregateProjectionContentsDto<SingleAggregateListProjectionDto<AggregateDto<TContents>>>(
            new SingleAggregateListProjectionDto<AggregateDto<TContents>> { List = aggregates },
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
    public async
        Task<MultipleAggregateProjectionContentsDto<
            SingleAggregateListProjectionDto<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>>
        GetSingleAggregateProjectionListObject<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>()
        where TAggregate : AggregateBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
    {
        var aggregates = Objects.Where(m => m.GetType().Name == typeof(TSingleAggregateProjection).Name)
            .Select(m => (SingleAggregateProjectionDto<TSingleAggregateProjectionContents>)m)
            .ToList();
        await Task.CompletedTask;
        return new
            MultipleAggregateProjectionContentsDto<
                SingleAggregateListProjectionDto<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>(
                new SingleAggregateListProjectionDto<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>> { List = aggregates },
                Guid.Empty,
                string.Empty,
                0,
                aggregates.Count);
    }
    public async Task<List<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>
        GetSingleAggregateProjectionList<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>(
            QueryListType queryListType = QueryListType.ActiveOnly) where TAggregate : AggregateBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
    {
        var aggregates = Objects.Where(m => m.GetType().Name == typeof(SingleAggregateProjectionDto<TSingleAggregateProjectionContents>).Name)
            .Select(m => (SingleAggregateProjectionDto<TSingleAggregateProjectionContents>)m)
            .ToList();
        await Task.CompletedTask;
        return aggregates;
    }
}
