using Sekiban.Pure.Aggregates;
namespace Pure.Domain;

public record Branch(string Name) : IAggregatePayload;
