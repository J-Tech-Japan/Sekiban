namespace Sekiban.Dcb.Orleans.Tests;

[GenerateSerializer]
public readonly record struct OptionalDateResultSurrogate(
    [property: Id(0)] bool HasValue,
    [property: Id(1)] DateOnly Value);