using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.TestHelpers;

public class MemoryMultipleAggregateProjectionService : IMultipleAggregateProjectionService
{
    public List<dynamic> Objects { get; init; } = new();

    public async Task<P> GetProjectionAsync<P>() where P : MultipleAggregateProjectionBase<P>, IMultipleAggregateProjectionDto, new()
    {
        await Task.CompletedTask;
        return Objects.FirstOrDefault(m => m is P) ?? throw new SekibanProjectionNotExistsException();
    }
    public async Task<SingleAggregateProjectionDto<AggregateDtoBase<TContents>>> GetAggregateListObject<T, TContents>()
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents
    {
        var aggregates = Objects.Where(m => m.Contents.GetType().Name == typeof(TContents).Name).Select(m => (AggregateDtoBase<TContents>)m).ToList();
        await Task.CompletedTask;
        return new SingleAggregateProjectionDto<AggregateDtoBase<TContents>>(aggregates, Guid.Empty, string.Empty, 0, aggregates.Count);
    }

    public async Task<List<AggregateDtoBase<TContents>>> GetAggregateList<T, TContents>(QueryListType queryListType = QueryListType.ActiveOnly)
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents
    {
        var aggregates = Objects.Where(m => m.Contents.GetType().Name == typeof(TContents).Name).Select(m => (AggregateDtoBase<TContents>)m).ToList();
        await Task.CompletedTask;
        return aggregates;
    }
    public async Task<SingleAggregateProjectionDto<T>> GetSingleAggregateProjectionListObject<T>() where T : SingleAggregateProjectionBase<T>, new()
    {
        var aggregates = Objects.Where(m => m.GetType().Name == typeof(T).Name).Select(m => (T)m).ToList();
        await Task.CompletedTask;
        return new SingleAggregateProjectionDto<T>(aggregates, Guid.Empty, string.Empty, 0, aggregates.Count);
    }

    public async Task<List<T>> GetSingleAggregateProjectionList<T>(QueryListType queryListType = QueryListType.ActiveOnly)
        where T : SingleAggregateProjectionBase<T>, new()
    {
        var aggregates = Objects.Where(m => m.GetType().Name == typeof(T).Name).Select(m => (T)m).ToList();
        await Task.CompletedTask;
        return aggregates;
    }
}
