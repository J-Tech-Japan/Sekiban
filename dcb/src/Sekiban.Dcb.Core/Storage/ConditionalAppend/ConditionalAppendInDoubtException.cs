namespace Sekiban.Dcb.Storage;

/// <summary>
///     The CLOSED, machine-readable set of reasons a conditional (unique-key) append is in-doubt. A closed enum (not an
///     open string) so callers can branch exhaustively and the reason contract cannot drift: new reasons are an additive,
///     reviewable change here, never an arbitrary string a call site invented.
/// </summary>
public enum ConditionalAppendInDoubtReason
{
    /// <summary>A claim conflict was signalled, but no committed winner could be read back to verify it.</summary>
    WinnerUnreadableAfterConflict = 1,

    /// <summary>The write was cancelled/timed out after possibly committing, and an authoritative read could not resolve it.</summary>
    AmbiguousAfterWrite = 2,

    /// <summary>The same-operation winner exists, but its contracted committed state could not be verified/repaired (transient).</summary>
    CommittedStateUnverified = 3
}

/// <summary>
///     The typed, machine-readable, RETRYABLE failure raised when a conditional (unique-key) append cannot be resolved to
///     a definite outcome: the storage state is in-doubt. This is NOT a fifth success/conflict
///     <see cref="ConditionalAppendStatus" /> — it is a failure the caller may safely retry, and a retry converges (once
///     the winner commits it classifies as <see cref="ConditionalAppendStatus.AlreadyCommittedSameOperation" />).
///     Distinct from a NON-retryable committed-state corruption (<see cref="ConditionalAppendCommittedStateCorruptionException" />).
///     Construction is only through the fixed factories, so the reason is always one of the closed
///     <see cref="ConditionalAppendInDoubtReason" /> values — a call site cannot supply an arbitrary code.
///     It is SECRET-SAFE: it carries only the provider name, the ServiceId (a service identifier, never a secret), and the
///     DERIVED storage identity (the deterministic EventId — a pure function of the key, not the raw key). The raw
///     idempotency key and the payload are never carried. The real provider conflict/transport/cancellation exception is
///     preserved as <see cref="Exception.InnerException" /> ONLY when one actually occurred.
/// </summary>
public sealed class ConditionalAppendInDoubtException : Exception
{
    private ConditionalAppendInDoubtException(
        string providerName,
        string serviceId,
        Guid derivedEventId,
        ConditionalAppendInDoubtReason reason,
        Exception? innerException)
        : base(
            $"Conditional (unique-key) append on '{providerName}' is in-doubt ({ReasonToCode(reason)}) for derived event "
            + $"id {derivedEventId} (service {serviceId}); the outcome could not be resolved. This is retryable — a retry "
            + "converges once the winner commits.",
            innerException)
    {
        ProviderName = providerName;
        ServiceId = serviceId;
        DerivedEventId = derivedEventId;
        Reason = reason;
    }

    /// <summary>Creates an in-doubt failure for one of the closed reasons. The only construction path.</summary>
    public static ConditionalAppendInDoubtException Create(
        string providerName,
        string serviceId,
        Guid derivedEventId,
        ConditionalAppendInDoubtReason reason,
        Exception? innerException = null) =>
        new(providerName, serviceId, derivedEventId, reason, innerException);

    /// <summary>Always true — an in-doubt conditional append is safe to retry.</summary>
    public bool IsRetryable => true;

    public string ProviderName { get; }
    public string ServiceId { get; }
    public Guid DerivedEventId { get; }
    public ConditionalAppendInDoubtReason Reason { get; }

    /// <summary>Stable, serialization-facing string code derived from the closed <see cref="Reason" />.</summary>
    public string ReasonCode => ReasonToCode(Reason);

    /// <summary>Maps the closed reason to its stable wire code. Unknown values fail closed rather than emit a silent string.</summary>
    public static string ReasonToCode(ConditionalAppendInDoubtReason reason) => reason switch
    {
        ConditionalAppendInDoubtReason.WinnerUnreadableAfterConflict => "winner-unreadable-after-conflict",
        ConditionalAppendInDoubtReason.AmbiguousAfterWrite => "ambiguous-after-write",
        ConditionalAppendInDoubtReason.CommittedStateUnverified => "committed-state-unverified",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown in-doubt reason.")
    };
}
