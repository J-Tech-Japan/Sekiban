namespace Sekiban.Dcb.Orleans.Surrogates;

[GenerateSerializer]
public struct ResultBoxSurrogate<T> where T : notnull
{
    [Id(0)]
    public bool IsSuccess { get; set; }

    [Id(1)]
    public T? Value { get; set; }

    [Id(2)]
    public string? ErrorMessage { get; set; }

    [Id(3)]
    public string? ExceptionType { get; set; }

    // Most ResultBox errors retain their existing compact string form. Projection faults are the deliberate exception:
    // operators need the typed reconstructed descriptor (including its annotations) on the CLIENT, so its dedicated
    // Orleans surrogate travels alongside the legacy error fields. Additive ids keep existing response payloads valid.
    [Id(4)]
    public bool IsProjectionFault { get; set; }

    [Id(5)]
    public Sekiban.Dcb.Orleans.Serialization.ProjectionFaultExceptionSurrogate ProjectionFault { get; set; }
}
