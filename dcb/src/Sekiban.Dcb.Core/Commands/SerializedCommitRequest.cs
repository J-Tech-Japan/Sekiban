using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     Commit request DTO: event candidates + consistency tag entries.
///     Sent by WASM clients to commit serialized events with consistency checks.
/// </summary>
public record SerializedCommitRequest(
    IReadOnlyList<SerializableEventCandidate> EventCandidates,
    IReadOnlyList<ConsistencyTagEntry> ConsistencyTags);
