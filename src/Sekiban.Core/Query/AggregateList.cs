using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query;

public class AggregateList<T> where T : IAggregateIdentifier
{
    public List<T> List { get; set; } = new();
    public Guid? LastEventId { get; set; } = null;
    public string LastSortableUniqueId { get; set; } = string.Empty;

    public static string UniqueKey() => $"AggregateList-{typeof(T).Name}";
}
