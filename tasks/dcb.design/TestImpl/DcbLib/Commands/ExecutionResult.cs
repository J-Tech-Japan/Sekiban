namespace DcbLib.Commands;

/// <summary>
/// Successful result of command execution
/// </summary>
public record ExecutionResult(
    Guid EventId,
    long EventPosition,
    IReadOnlyList<TagWriteResult> TagWrites,
    TimeSpan Duration,
    Dictionary<string, object>? Metadata = null
);