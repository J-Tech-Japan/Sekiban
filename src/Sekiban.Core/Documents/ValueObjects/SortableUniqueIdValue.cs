using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Documents.ValueObjects;

public record SortableUniqueIdValue(string Value)
{
    public const int SafeMilliseconds = 5000;
    public const int TickNumberOfLength = 19;
    public const int IdNumberOfLength = 11;
    public const string TickFormatter = "0000000000000000000";
    public const string IdFormatter = "00000000000";
    public static readonly long IdModBase = (long)Math.Pow(10, IdNumberOfLength);

    public DateTime GetTicks()
    {
        var ticksString = Value?.Substring(0, TickNumberOfLength) ?? TickFormatter;
        var ticks = long.Parse(ticksString);
        return new DateTime(ticks);
    }

    public static implicit operator string(SortableUniqueIdValue vo) => vo.Value;

    public static implicit operator SortableUniqueIdValue(string v) => new(v);

    public static string Generate(DateTime timestamp, Guid id) => GetTickString(timestamp.Ticks) + GetIdString(id);

    public static string GetSafeIdFromUtc() =>
        GetTickString(SekibanDateProducer.GetRegistered().UtcNow.AddMilliseconds(-SafeMilliseconds).Ticks) +
        GetIdString(Guid.Empty);
    public static string GetCurrentIdFromUtc() =>
        GetTickString(SekibanDateProducer.GetRegistered().UtcNow.Ticks) +
        GetIdString(Guid.Empty);

    public string GetSafeId() => GetTicks().AddSeconds(-SafeMilliseconds).Ticks + GetIdString(Guid.Empty);

    public bool EarlierThan(SortableUniqueIdValue toCompare) => Value.CompareTo(toCompare) < 0;

    public bool LaterThan(SortableUniqueIdValue toCompare) => Value.CompareTo(toCompare) > 0;

    public static string GetTickString(long tick) => tick.ToString(TickFormatter);
    public static string GetIdString(Guid id) => (Math.Abs(id.GetHashCode()) % IdModBase).ToString(IdFormatter);
    public static SortableUniqueIdValue? GetShouldIncludeSortableUniqueIdValue(object target)
    {
        if (target is IShouldIncludesSortableUniqueId sortableUniqueId)
        {
            return sortableUniqueId.IncludesSortableUniqueIdValue != null ? new SortableUniqueIdValue(sortableUniqueId.IncludesSortableUniqueIdValue)
                : null;
        }
        return null;
    }
    public static SortableUniqueIdValue? NullableValue(string? value) => value != null ? new SortableUniqueIdValue(value) : null;
}
