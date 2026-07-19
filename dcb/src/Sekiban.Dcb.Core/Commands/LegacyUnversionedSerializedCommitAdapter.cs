namespace Sekiban.Dcb.Commands;

/// <summary>
///     SEK-G17 lossless adapter from the legacy version-less official shape (<see cref="SerializedCommitRequest" />) to the
///     V1 envelope. This is provably safe because the official contract never changed across dcb-v10.2.2 → 10.6.0: the lift
///     copies the event candidates and consistency tags verbatim, so each event's heterogeneous per-event
///     <see cref="Sekiban.Dcb.Events.SerializableEventCandidate.Tags" /> is preserved by construction. It NEVER passes
///     through a per-commit-tag model (that shape belongs to a downstream runtime contract, not this one).
/// </summary>
public static class LegacyUnversionedSerializedCommitAdapter
{
    /// <summary>Lifts a legacy unversioned request into the current V1 envelope with no data loss.</summary>
    public static VersionedSerializedCommitRequest ToVersionedV1(SerializedCommitRequest legacy) =>
        new(VersionedSerializedCommitRequest.CurrentVersion, legacy.EventCandidates, legacy.ConsistencyTags);
}
