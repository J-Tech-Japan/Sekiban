namespace Sekiban.Pure.Orleans.Surrogates;

[GenerateSerializer]
public record struct SekibanQueryTypeExceptionSurrogate(
    [property: Id(0)] string Message);
