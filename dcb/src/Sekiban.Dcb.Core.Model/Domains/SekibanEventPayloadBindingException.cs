namespace Sekiban.Dcb.Domains;

/// <summary>
///     Thrown when a stored event payload cannot be bound to its CLR type in a way that would otherwise have passed
///     silently — the case-only member mismatch of issue #1074, an ambiguous set of declared names, a member that a
///     strict policy will not accept, or a metadata-declared required member that is missing.
///     It carries what an operator needs to find the bad row and nothing they must not see: the event type name, the
///     CLR type, the JSON member name that offended and the declared name it was closest to, and where in the payload
///     it sat. <b>It never carries a payload value.</b> The diagnosis is "a member called X should have been x", not
///     "the value was Y".
/// </summary>
public sealed class SekibanEventPayloadBindingException : Exception
{
    /// <summary>Creates the exception with a fully-formed, secret-free message.</summary>
    public SekibanEventPayloadBindingException(
        string message,
        string eventTypeName,
        string clrTypeName,
        string? offendingJsonName,
        string? expectedJsonName,
        string? payloadLocation,
        Exception? innerException = null) : base(message, innerException)
    {
        EventTypeName = eventTypeName;
        ClrTypeName = clrTypeName;
        OffendingJsonName = offendingJsonName;
        ExpectedJsonName = expectedJsonName;
        PayloadLocation = payloadLocation;
    }

    /// <summary>The registered event type name whose payload failed to bind, e.g. <c>StudentCreated</c>.</summary>
    public string EventTypeName { get; }

    /// <summary>The full name of the CLR type the payload was being bound to.</summary>
    public string ClrTypeName { get; }

    /// <summary>The JSON member name that could not be bound, when there is one specific member to name.</summary>
    public string? OffendingJsonName { get; }

    /// <summary>The declared member name the offender was closest to — e.g. the correctly-cased name it should have been.</summary>
    public string? ExpectedJsonName { get; }

    /// <summary>Where in the payload the problem was, as a JSON path, e.g. <c>$.studentId</c>. Top-level only.</summary>
    public string? PayloadLocation { get; }
}
