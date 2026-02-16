using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     Commit result DTO: written events + tag write results + duration.
///     Returned by the server after successfully committing serialized events.
/// </summary>
public record SerializedCommitResult(
    IReadOnlyList<SerializableEvent> WrittenEvents,
    IReadOnlyList<TagWriteResult> TagWriteResults,
    TimeSpan Duration);
