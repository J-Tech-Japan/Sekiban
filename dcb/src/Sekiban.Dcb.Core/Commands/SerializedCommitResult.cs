using Sekiban.Dcb.Events;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     Commit result DTO: written events + tag write results + duration.
///     Returned by the server after successfully committing serialized events.
/// </summary>
public record SerializedCommitResult(
    IReadOnlyList<SerializableEvent> WrittenEvents,
    IReadOnlyList<TagWriteResult> TagWriteResults,
    TimeSpan Duration)
{
    /// <summary>Structured non-strict size-gate diagnostics; empty means every configured scope validated.</summary>
    public IReadOnlyList<ExecutorSizeDiagnostic> SizeGateDiagnostics { get; init; } = Array.Empty<ExecutorSizeDiagnostic>();
}
