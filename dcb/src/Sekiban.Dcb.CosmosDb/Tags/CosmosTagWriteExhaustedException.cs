namespace Sekiban.Dcb.CosmosDb.Tags;

/// <summary>
///     Raised under <see cref="CosmosWriteFailurePolicy.RollForward" /> when the tag write kept failing until
///     the retries ran out or the deadline passed.
///     The events are durable and were NOT deleted: they are visible to all-events readers, but some of their
///     tag rows may be missing, so tag-scoped reads and tag projectors will not see them yet. Tag rows derive
///     deterministically from the events (see <see cref="CosmosTagIdentity" />), so <see cref="EventIds" />
///     is exactly the set a repair pass needs in order to complete the index.
/// </summary>
public class CosmosTagWriteExhaustedException : Exception
{
    /// <summary>
    ///     Creates a tag-write exhaustion exception.
    /// </summary>
    public CosmosTagWriteExhaustedException()
    {
    }

    /// <summary>
    ///     Creates a tag-write exhaustion exception with a message.
    /// </summary>
    public CosmosTagWriteExhaustedException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Creates a tag-write exhaustion exception with a message and inner exception.
    /// </summary>
    public CosmosTagWriteExhaustedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    ///     Creates a tag-write exhaustion exception naming the events whose tag rows may be incomplete.
    /// </summary>
    public CosmosTagWriteExhaustedException(
        IReadOnlyList<Guid> eventIds,
        int attempts,
        Exception innerException)
        : base(
            $"Tag write failed after {attempts} attempt(s). The {eventIds?.Count ?? 0} event(s) written by this " +
            "call were NOT deleted and remain visible to all-events readers, but some of their tag rows may be " +
            "missing, so tag-scoped reads will not see them until the index is repaired. Affected event ids: " +
            $"{string.Join(", ", eventIds ?? Array.Empty<Guid>())}.",
            innerException)
    {
        EventIds = eventIds ?? Array.Empty<Guid>();
        Attempts = attempts;
    }

    /// <summary>
    ///     Events that are durable but whose tag rows may be incomplete.
    /// </summary>
    public IReadOnlyList<Guid> EventIds { get; } = Array.Empty<Guid>();

    /// <summary>
    ///     Number of tag-write attempts made before giving up.
    /// </summary>
    public int Attempts { get; }
}
