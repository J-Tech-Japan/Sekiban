using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries;

public class SingleAggregateList<T> where T : ISingleAggregate
{
    public List<T> List { get; set; } = new();
    public Guid? LastEventId { get; set; } = null;
    public string LastSortableUniqueId { get; set; } = string.Empty;

    public static string UniqueKey() =>
        $"AggregateList-{typeof(T).Name}";
}
