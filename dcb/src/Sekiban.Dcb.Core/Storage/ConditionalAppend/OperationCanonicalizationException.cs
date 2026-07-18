namespace Sekiban.Dcb.Storage;

/// <summary>
///     The typed failure raised (fail-closed, before any store side effect) when a conditional append's operation cannot
///     be reduced to its canonical fingerprint: the event type is not registered / not authoritative, the payload cannot
///     be deserialized into that type, or the canonicalization version is unsupported. Because it is raised before the
///     write, a request that cannot be canonicalized never produces a durable claim. A provider exception (e.g. a JSON
///     parse failure) is preserved as the inner cause when one occurred.
/// </summary>
public sealed class OperationCanonicalizationException : Exception
{
    public OperationCanonicalizationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
