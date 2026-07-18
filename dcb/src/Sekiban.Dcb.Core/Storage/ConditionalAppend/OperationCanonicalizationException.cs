namespace Sekiban.Dcb.Storage;

/// <summary>
///     The typed failure raised (fail-closed, before any store side effect) when a conditional append's operation cannot
///     be reduced to its canonical fingerprint: the event type is not registered / not authoritative, or the payload
///     cannot be deserialized/canonicalized into that type. Because it is raised before the write, a request that cannot
///     be canonicalized never produces a durable claim.
///     SECRET-SAFE by construction: this exception carries NO inner exception and NO caller payload/key — only a sanitized
///     message whose sole caller-supplied datum is the registered event-type NAME (safe metadata). A converter/
///     deserializer exception (which may embed the raw payload or key in its message, Data, or stack) is deliberately
///     discarded rather than chained here, so it can never surface through the ResultBox.Error graph. There is no
///     inner-exception constructor for exactly this reason.
/// </summary>
public sealed class OperationCanonicalizationException : Exception
{
    public OperationCanonicalizationException(string message) : base(message)
    {
    }
}
