using Orleans;
using Sekiban.Pure.Aggregates;
namespace AspireEventSample.Domain.Aggregates.Branches;

[GenerateSerializer]
public record Branch(string Name, string Country) : IAggregatePayload;