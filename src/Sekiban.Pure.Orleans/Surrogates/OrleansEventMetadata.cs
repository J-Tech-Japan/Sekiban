namespace Sekiban.Pure.Orleans.Surrogates;

[GenerateSerializer]
public record struct OrleansEventMetadata(
    [property: Id(0)] string CausationId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] string ExecutedUser);