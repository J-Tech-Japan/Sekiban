namespace Sekiban.Dcb.Common;

/// <summary>
///     Calculate helper value for Sortable Unique Id
///     SortableUniqueId contains 19 digit tick and 11 digit random number
///     It can be sorted globally in the system.
/// </summary>
public record SortableUniqueId(string Value)
{
    private static readonly ISortableUniqueIdGenerator ProcessSharedGenerator =
        new MonotonicSortableUniqueIdGenerator();

    internal static ISortableUniqueIdGenerator GetProcessSharedGenerator() => ProcessSharedGenerator;
    public const int SafeMilliseconds = 5000;
    public const int TickNumberOfLength = 19;
    public const int IdNumberOfLength = 11;
    public const string TickFormatter = "0000000000000000000";
    public const string IdFormatter = "00000000000";
    public static readonly long IdModBase = (long)Math.Pow(10, IdNumberOfLength);

    /// <summary>
    ///     Minimum value for SortableUniqueId
    /// </summary>
    public static SortableUniqueId MinValue => Generate(DateTime.MinValue, Guid.Empty);

    /// <summary>
    ///     Maximum value for SortableUniqueId
    /// </summary>
    public static SortableUniqueId MaxValue => Generate(
        DateTime.MaxValue,
        new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"));

    /// <summary>
    ///     Get Datetime from Sortable Unique Id
    /// </summary>
    public DateTime GetDateTime()
    {
        var ticksString = Value[..TickNumberOfLength];
        var ticks = long.Parse(ticksString);
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    /// <summary>
    ///     Get the ID part (last 11 digits) as a string
    /// </summary>
    public string GetIdPart() =>
        Value.Length >= TickNumberOfLength + IdNumberOfLength
            ? Value.Substring(TickNumberOfLength, IdNumberOfLength)
            : "";

    /// <summary>
    ///     Sortable Unique Id to String implicit conversion
    /// </summary>
    public static implicit operator string(SortableUniqueId vo) => vo.Value;

    /// <summary>
    ///     String to Sortable Unique Id implicit conversion
    /// </summary>
    public static implicit operator SortableUniqueId(string v) => new(v);

    /// <summary>
    ///     Generate Sortable Unique Id from datetime and guid.
    /// </summary>
    public static string Generate(DateTime timestamp, Guid id) => GetTickString(timestamp.Ticks) + GetIdString(id);

    /// <summary>
    ///     Generate Sortable Unique Id from Current UTC time and new Guid
    /// </summary>
    public static string GenerateNew() => ProcessSharedGenerator.GenerateNew();

    /// <summary>
    ///     Generate Safe Sortable Unique Id from Current UTC.
    ///     Safe Sortable Unique Id means data until 5 seconds from now.
    ///     Which means no new item will be added in usual situation before sortable unique id
    /// </summary>
    public static string GetSafeIdFromUtc() =>
        GetTickString(DateTime.UtcNow.AddMilliseconds(-SafeMilliseconds).Ticks) + GetIdString(Guid.Empty);

    /// <summary>
    ///     Generate Sortable Unique Id from Current UTC.
    /// </summary>
    public static string GetCurrentIdFromUtc() =>
        GetTickString(DateTime.UtcNow.Ticks) + GetIdString(Guid.Empty);

    /// <summary>
    ///     Generate Safe Sortable Unique Id from this instance's time.
    ///     Safe Sortable Unique Id means data until 5 seconds from the time.
    /// </summary>
    public string GetSafeId() =>
        GetTickString(GetDateTime().AddMilliseconds(-SafeMilliseconds).Ticks) + GetIdString(Guid.Empty);

    /// <summary>
    ///     object that call this function "Is Earlier than toCompare object passed as parameter"
    /// </summary>
    public bool IsEarlierThan(SortableUniqueId toCompare) => Value.CompareTo(toCompare.Value) < 0;

    /// <summary>
    ///     object that call this function "Is Earlier than or Equal toCompare object passed as parameter"
    /// </summary>
    public bool IsEarlierThanOrEqual(SortableUniqueId toCompare) => Value.CompareTo(toCompare.Value) <= 0;

    /// <summary>
    ///     object that call this function "Is Later than or Equal toCompare object passed as parameter"
    /// </summary>
    public bool IsLaterThanOrEqual(SortableUniqueId toCompare) => Value.CompareTo(toCompare.Value) >= 0;

    /// <summary>
    ///     object that call this function "Is Later than toCompare object passed as parameter"
    /// </summary>
    public bool IsLaterThan(SortableUniqueId toCompare) => Value.CompareTo(toCompare.Value) > 0;

    /// <summary>
    ///     Get Tick String from Tick Long Value
    /// </summary>
    public static string GetTickString(long tick) => tick.ToString(TickFormatter);

    /// <summary>
    ///     Get Random string from Guid Id Value
    /// </summary>
    public static string GetIdString(Guid id) => (Math.Abs(id.GetHashCode()) % IdModBase).ToString(IdFormatter);

    /// <summary>
    ///     Create nullable SortableUniqueId from nullable string
    /// </summary>
    public static SortableUniqueId? NullableValue(string? value) =>
        value != null ? new SortableUniqueId(value) : null;

    /// <summary>
    ///     Parse string to SortableUniqueId with validation
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is not a valid sortable unique id.</exception>
    public static SortableUniqueId Parse(string value)
    {
        var error = Validate(value);
        return error is null ? new SortableUniqueId(value) : throw new ArgumentException(error);
    }

    /// <summary>
    ///     Try to parse string to SortableUniqueId
    /// </summary>
    public static bool TryParse(string value, out SortableUniqueId? result)
    {
        if (Validate(value) is null)
        {
            result = new SortableUniqueId(value);
            return true;
        }

        result = null;
        return false;
    }

    private static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Value cannot be null or empty";

        if (value.Length != TickNumberOfLength + IdNumberOfLength)
            return $"Value must be exactly {TickNumberOfLength + IdNumberOfLength} characters long";

        // Validate that all characters are digits
        if (!value.All(char.IsDigit))
            return "Value must contain only digits";

        return null;
    }
}
