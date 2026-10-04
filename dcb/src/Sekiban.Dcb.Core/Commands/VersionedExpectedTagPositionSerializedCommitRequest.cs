using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Commands;

/// <summary>
///     Additive V2 serialized commit envelope for PostgreSQL's store-enforced multi-tag expected-position CAS. V1 and
///     the legacy unversioned payload deliberately remain untouched: their omission semantics continue to mean the
///     existing reservation-only, no-enforcement flow.
/// </summary>
public sealed record VersionedExpectedTagPositionSerializedCommitRequest(
    int Version,
    IReadOnlyList<SerializableEventCandidate> EventCandidates,
    IReadOnlyList<ConsistencyTagEntry> ConsistencyTags,
    IReadOnlyList<TagHeadExpectationEntry> ExpectedTagPositions)
{
    /// <summary>The only supported version for this additive expected-position envelope.</summary>
    public const int CurrentVersion = 2;
}
