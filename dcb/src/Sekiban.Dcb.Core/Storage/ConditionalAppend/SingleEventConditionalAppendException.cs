namespace Sekiban.Dcb.Storage;

/// <summary>
///     The typed contract failure raised when a conditional (unique-key) command does not append EXACTLY ONE event. The
///     conditional contract is single-event; both zero and more-than-one fail closed BEFORE any store call — a zero-event
///     conditional command must never fall through to the legacy empty-success result.
/// </summary>
public sealed class SingleEventConditionalAppendException : Exception
{
    public SingleEventConditionalAppendException(int appendedEventCount)
        : base(
            "Conditional (unique-key) append requires the handler to append exactly one event, but it appended "
            + $"{appendedEventCount}. Zero and multiple events both fail closed.")
    {
        AppendedEventCount = appendedEventCount;
    }

    public int AppendedEventCount { get; }
}
