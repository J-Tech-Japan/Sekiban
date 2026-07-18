namespace Sekiban.Dcb.Storage;

/// <summary>
///     The typed, machine-readable, RETRYABLE failure raised when a conditional (unique-key) append cannot be resolved to
///     a definite outcome: the storage state is in-doubt. This is NOT a fifth success/conflict
///     <see cref="ConditionalAppendStatus" /> — it is a failure the caller may safely retry, and a retry converges
///     (once the winner commits it classifies as <see cref="ConditionalAppendStatus.AlreadyCommittedSameOperation" />).
///     It is raised when:
///     <list type="bullet">
///         <item>a claim conflict was signalled but no committed winner could be read back to verify it, or</item>
///         <item>the write was cancelled/timed out after possibly committing and an authoritative read could not
///         establish the committed state, or</item>
///         <item>the same-operation winner exists but its contracted committed state (e.g. every required tag row) could
///         not be verified/repaired.</item>
///     </list>
///     Following the G9/G14 contract it is <c>ResultBox.Error</c> on the WithResult facade and rethrown as-is by the
///     WithoutResult guarded boundary. It is SECRET-SAFE: it carries only the provider name, the ServiceId (a service
///     identifier, never a secret), and the DERIVED storage identity (the deterministic EventId — a pure function of the
///     key, not the raw key). The raw idempotency key and the payload are never carried. The real provider
///     conflict/transport/cancellation exception is preserved as <see cref="Exception.InnerException" /> ONLY when one
///     actually occurred.
/// </summary>
public sealed class ConditionalAppendInDoubtException : Exception
{
    /// <summary>Stable machine-readable reason codes, so callers can branch without string parsing.</summary>
    public const string ReasonWinnerUnreadable = "winner-unreadable-after-conflict";
    public const string ReasonAmbiguousAfterWrite = "ambiguous-after-write";
    public const string ReasonCommittedStateUnverified = "committed-state-unverified";

    public ConditionalAppendInDoubtException(
        string providerName,
        string serviceId,
        Guid derivedEventId,
        string reasonCode,
        Exception? innerException = null)
        : base(
            $"Conditional (unique-key) append on '{providerName}' is in-doubt ({reasonCode}) for derived event id "
            + $"{derivedEventId} (service {serviceId}); the outcome could not be resolved. This is retryable — a retry "
            + "converges once the winner commits.",
            innerException)
    {
        ProviderName = providerName;
        ServiceId = serviceId;
        DerivedEventId = derivedEventId;
        ReasonCode = reasonCode;
    }

    /// <summary>Always true — an in-doubt conditional append is safe to retry.</summary>
    public bool IsRetryable => true;

    public string ProviderName { get; }
    public string ServiceId { get; }
    public Guid DerivedEventId { get; }
    public string ReasonCode { get; }
}
