using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query;

public class SingleAggregateList<T> where T : ISingleAggregate
{
    public List<T> List { get; set; } = new();
    public Guid? LastEventId { get; set; } = null;
    public string LastSortableUniqueId { get; set; } = string.Empty;

    public static string UniqueKey()
    {
        return $"AggregateList-{typeof(T).Name}";
    }
}