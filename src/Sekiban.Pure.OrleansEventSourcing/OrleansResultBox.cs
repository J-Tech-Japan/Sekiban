using System.Text.Json.Serialization;
using ResultBoxes;

namespace Sekiban.Pure.OrleansEventSourcing;

[GenerateSerializer]
public record OrleansResultBox<TValue>(System.Exception? Exception,TValue? Value) where TValue : notnull
{
    [JsonIgnore] public bool IsSuccess => Exception is null && Value is not null;
    public System.Exception GetException() =>
        Exception ?? throw new ResultsInvalidOperationException("no exception");

    public TValue GetValue() =>
        (IsSuccess ? Value : throw new ResultsInvalidOperationException("no value")) ??
        throw new ResultsInvalidOperationException();
}