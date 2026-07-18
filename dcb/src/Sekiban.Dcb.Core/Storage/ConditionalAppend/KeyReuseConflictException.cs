namespace Sekiban.Dcb.Storage;

/// <summary>
///     The typed intentional failure for <see cref="ConditionalAppendStatus.KeyReuseConflict" />: the idempotency key is
///     already claimed, but by a DIFFERENT operation (the persisted fingerprint differs from this attempt's). Following
///     the G9/G14 contract this is returned as <c>ResultBox.Error</c> on the WithResult facade and rethrown as-is by the
///     WithoutResult guarded boundary.
///     The raw idempotency key is deliberately NOT carried — only the opaque fingerprints, so diagnostics never leak the
///     key. A provider exception (e.g. a unique-violation surfaced by a real store) is preserved as
///     <see cref="Exception.InnerException" /> ONLY when one actually occurred; a conflict discovered by read (as the
///     in-memory reference does) has no provider exception and none is fabricated.
/// </summary>
public sealed class KeyReuseConflictException : Exception
{
    public KeyReuseConflictException(
        string existingFingerprint,
        string attemptedFingerprint,
        string providerName,
        Exception? innerException = null)
        : base(
            $"Idempotency key is already claimed by a different operation on '{providerName}' "
            + $"(existing fingerprint {existingFingerprint}, attempted {attemptedFingerprint}).",
            innerException)
    {
        ExistingFingerprint = existingFingerprint;
        AttemptedFingerprint = attemptedFingerprint;
        ProviderName = providerName;
    }

    public ConditionalAppendStatus Status => ConditionalAppendStatus.KeyReuseConflict;
    public string ExistingFingerprint { get; }
    public string AttemptedFingerprint { get; }
    public string ProviderName { get; }
}
