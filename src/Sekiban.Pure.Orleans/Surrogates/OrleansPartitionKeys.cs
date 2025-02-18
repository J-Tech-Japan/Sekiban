namespace Sekiban.Pure.Orleans.Surrogates;

[GenerateSerializer]
public record struct OrleansPartitionKeys(
    [property: Id(0)] Guid AggregateId,
    [property: Id(1)] string Group,
    [property: Id(2)] string RootPartitionKey);