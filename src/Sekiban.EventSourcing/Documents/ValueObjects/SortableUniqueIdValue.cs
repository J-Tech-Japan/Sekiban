using Sekiban.EventSourcing.Shared;
namespace Sekiban.EventSourcing.Documents.ValueObjects;

public record SortableUniqueIdValue(string Value)
{
    public static int safeMilliseconds = 5000;

    public DateTime GetTicks()
    {
        var ticksString = Value?.Substring(0, 18) ?? "000000000000000000";
        var ticks = long.Parse(ticksString);
        return new DateTime(ticks);
    }
    public static implicit operator string(SortableUniqueIdValue vo)
    {
        return vo.Value;
    }
    public static implicit operator SortableUniqueIdValue(string v)
    {
        return new(v);
    }
    public static string Generate(DateTime timestamp, Guid id)
    {
        return timestamp.Ticks + (Math.Abs(id.GetHashCode()) % 1000000000000).ToString("000000000000");
    }
    public static string GetSafeIdFromUtc()
    {
        return SekibanDateProducer.GetRegistered().UtcNow.AddMilliseconds(-safeMilliseconds).Ticks +
            (Math.Abs(Guid.Empty.GetHashCode()) % 1000000000000).ToString("000000000000");
    }
    public string GetSafeId()
    {
        return GetTicks().AddSeconds(-safeMilliseconds).Ticks + (Math.Abs(Guid.Empty.GetHashCode()) % 1000000000000).ToString("000000000000");
    }

    public bool EarlierThan(SortableUniqueIdValue toCompare)
    {
        return Value.CompareTo(toCompare) < 0;
    }
    public bool LaterThan(SortableUniqueIdValue toCompare)
    {
        return Value.CompareTo(toCompare) > 0;
    }
}
