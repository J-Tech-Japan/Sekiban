namespace Sekiban.EventSourcing.Documents;

public static class SortableUniqueIdGenerator
{
    public static string Generate(DateTime timestamp, Guid id) =>
        timestamp.Ticks + (Math.Abs(id.GetHashCode()) % 1000000000000).ToString("000000000000");
}
