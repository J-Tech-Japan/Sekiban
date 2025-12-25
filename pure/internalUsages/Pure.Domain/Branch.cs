using Sekiban.Pure.Aggregates;
namespace Pure.Domain;

[GenerateSerializer]
public record Branch(string Name) : IAggregatePayload;
