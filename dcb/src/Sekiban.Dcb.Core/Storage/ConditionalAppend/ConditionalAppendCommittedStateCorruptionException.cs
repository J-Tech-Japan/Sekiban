namespace Sekiban.Dcb.Storage;

/// <summary>
///     The typed, NON-retryable failure raised when a same-operation retry finds the winner's committed state corrupt: a
///     required index/tag row already exists under the deterministic identity but its content disagrees with what the
///     event contracts (a strict content mismatch, not a missing row). Unlike a missing row — which the committed-state
///     gate idempotently repairs, and whose transient repair failure is a retryable
///     <see cref="ConditionalAppendInDoubtException" /> — a disagreeing row is an integrity violation that must NOT be
///     overwritten and MUST NOT be retried indefinitely. It is surfaced so an operator investigates; the original provider
///     integrity exception is preserved as <see cref="Exception.InnerException" /> when one occurred.
///     SECRET-SAFE: carries only the provider name, the ServiceId, and the DERIVED storage identity (the deterministic
///     EventId — never the raw idempotency key or payload). Following the G9/G14 contract it is <c>ResultBox.Error</c> on
///     the WithResult facade and rethrown as-is by the WithoutResult guarded boundary.
/// </summary>
public sealed class ConditionalAppendCommittedStateCorruptionException : Exception
{
    public ConditionalAppendCommittedStateCorruptionException(
        string providerName,
        string serviceId,
        Guid derivedEventId,
        Exception? innerException = null)
        : base(
            $"Conditional (unique-key) append on '{providerName}' found the committed state corrupt for derived event id "
            + $"{derivedEventId} (service {serviceId}): an existing index/tag row disagrees with the event. It was NOT "
            + "overwritten. This is not retryable — investigate the integrity violation.",
            innerException)
    {
        ProviderName = providerName;
        ServiceId = serviceId;
        DerivedEventId = derivedEventId;
    }

    /// <summary>Always false — corruption is an integrity violation, not a transient to retry.</summary>
    public bool IsRetryable => false;

    public string ProviderName { get; }
    public string ServiceId { get; }
    public Guid DerivedEventId { get; }
}
