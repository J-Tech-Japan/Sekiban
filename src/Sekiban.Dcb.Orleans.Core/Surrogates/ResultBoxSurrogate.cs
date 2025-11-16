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
}
