using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     SEK-G17 additive, explicitly versioned envelope for the official multi-event serialized-commit wire contract. It
///     carries the SAME payload as the legacy positional <see cref="SerializedCommitRequest" /> (heterogeneous per-event
///     <see cref="SerializableEventCandidate.Tags" /> and <see cref="ConsistencyTagEntry" /> reservations) plus an explicit
///     <see cref="Version" /> discriminator so the contract can evolve without a positional break.
///     <para>
///         This is a NEW type — the legacy <see cref="SerializedCommitRequest" /> is left untouched, and G15's
///         single-event <c>SerializedConditionalCommitRequest</c> (IdempotencyKey-mandatory) is intentionally NOT reused as
///         the base envelope. Acceptance reads <see cref="Version" /> before binding this typed payload (two-phase), and a
///         known version routes with identical semantics to <see cref="Sekiban.Dcb.Actors.ISerializedSekibanDcbExecutor" />.
///     </para>
/// </summary>
public record VersionedSerializedCommitRequest(
    int Version,
    IReadOnlyList<SerializableEventCandidate> EventCandidates,
    IReadOnlyList<ConsistencyTagEntry> ConsistencyTags)
{
    /// <summary>The current (and only) supported wire version of this envelope.</summary>
    public const int CurrentVersion = 1;
}
