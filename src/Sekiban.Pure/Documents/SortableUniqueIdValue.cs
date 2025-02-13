using ResultBoxes;
using Sekiban.Core.Shared;
namespace Sekiban.Pure.Documents;

/// <summary>
///     Calculate helper value document for Sortable Unique Id
///     SortableUniqueId contains 19 digit tick and 11 digit random number
///     It can be sorted globally in the document.
/// </summary>
/// <param name="Value"></param>
public record SortableUniqueIdValue(string Value)
{
    public const int SafeMilliseconds = 5000;
    public const int TickNumberOfLength = 19;
    public const int IdNumberOfLength = 11;
    public const string TickFormatter = "0000000000000000000";
    public const string IdFormatter = "00000000000";
    public static readonly long IdModBase = (long)Math.Pow(10, IdNumberOfLength);

    /// <summary>
    ///     Get Datetime from Sortable Unique Id
    /// </summary>
    /// <returns></returns>
    public DateTime GetTicks()
    {
        var ticksString = Value[..TickNumberOfLength];
        var ticks = long.Parse(ticksString);
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    /// <summary>
    ///     Sortable Unique Id to String implicit conversion
    /// </summary>
    /// <param name="vo"></param>
    /// <returns></returns>
    public static implicit operator string(SortableUniqueIdValue vo) => vo.Value;

    /// <summary>
    ///     String to Sortable Unique Id implicit conversion
    /// </summary>
    /// <param name="v"></param>
    /// <returns></returns>
    public static implicit operator SortableUniqueIdValue(string v) => new(v);

    /// <summary>
    ///     Generate Sortable Unique Id from datetime and guid.
    /// </summary>
    /// <param name="timestamp"></param>
    /// <param name="id"></param>
    /// <returns></returns>
    public static string Generate(DateTime timestamp, Guid id) => GetTickString(timestamp.Ticks) + GetIdString(id);

    /// <summary>
    ///     Generate Safe Sortable Unique Id from Current UTC.
    ///     Safe Sortable Unique Id means data until 5 seconds from now.
    ///     Which means no new item will be added in usual situation before sortable unique id
    ///     UTC will be retrieve from <see cref="SekibanDateProducer" />
    /// </summary>
    /// <returns></returns>
    public static string GetSafeIdFromUtc() =>
        GetTickString(SekibanDateProducer.GetRegistered().UtcNow.AddMilliseconds(-SafeMilliseconds).Ticks) +
        GetIdString(Guid.Empty);

    /// <summary>
    ///     Generate Sortable Unique Id from Current UTC.
    ///     UTC will be retrieve from <see cref="SekibanDateProducer" />
    /// </summary>
    /// <returns></returns>
    public static string GetCurrentIdFromUtc() =>
        GetTickString(SekibanDateProducer.GetRegistered().UtcNow.Ticks) + GetIdString(Guid.Empty);

    /// <summary>
    ///     Generate Safe Sortable Unique Id from Current UTC.
    ///     Safe Sortable Unique Id means data until 5 seconds from now.
    ///     Which means no new item will be added in usual situation before sortable unique id
    ///     UTC will be retrieve from <see cref="SekibanDateProducer" />
    /// </summary>
    /// <returns></returns>
    public string GetSafeId() => GetTicks().AddSeconds(-SafeMilliseconds).Ticks + GetIdString(Guid.Empty);

    /// <summary>
    ///     object that call this function "Is Earlier than toCompare object passed as parameter"
    /// </summary>
    /// <param name="toCompare"></param>
    /// <returns></returns>
    public bool IsEarlierThan(SortableUniqueIdValue toCompare) => Value.CompareTo(toCompare) < 0;

    /// <summary>
    ///     object that call this function "Is Earlier than or Equal toCompare object passed as parameter"
    /// </summary>
    /// <param name="toCompare"></param>
    /// <returns></returns>
    public bool IsEarlierThanOrEqual(SortableUniqueIdValue toCompare) => Value.CompareTo(toCompare) <= 0;

    /// <summary>
    ///     object that call this function "Is Later than or Equal toCompare object passed as parameter"
    /// </summary>
    /// <param name="toCompare"></param>
    /// <returns></returns>
    public bool IsLaterThanOrEqual(SortableUniqueIdValue toCompare) => Value.CompareTo(toCompare) >= 0;
    public bool IsLaterThan(SortableUniqueIdValue toCompare) => Value.CompareTo(toCompare) > 0;

    /// <summary>
    ///     Get Tick String from Tick Long Value
    /// </summary>
    /// <param name="tick"></param>
    /// <returns></returns>
    public static string GetTickString(long tick) => tick.ToString(TickFormatter);

    /// <summary>
    ///     Get Random string from Guid Id Value
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static string GetIdString(Guid id) => (Math.Abs(id.GetHashCode()) % IdModBase).ToString(IdFormatter);

    public static SortableUniqueIdValue? NullableValue(string? value) =>
        value != null ? new SortableUniqueIdValue(value) : null;

    public static OptionalValue<SortableUniqueIdValue> OptionalValue(string? value) =>
        !string.IsNullOrWhiteSpace(value)
            ? new SortableUniqueIdValue(value)
            : OptionalValue<SortableUniqueIdValue>.Empty;
}