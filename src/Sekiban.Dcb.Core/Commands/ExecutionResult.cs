using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     Successful result of command execution
/// </summary>
public record ExecutionResult(
    Guid EventId,
    long EventPosition,
    IReadOnlyList<TagWriteResult> TagWrites,
    TimeSpan Duration,
    IEnumerable<Event> Events,
    Dictionary<string, object>? Metadata = null,
    string? SortableUniqueId = null
    );
