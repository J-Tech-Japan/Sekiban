using Orleans;
using Sekiban.Pure.Aggregates;
namespace Pure.Domain;

[GenerateSerializer]
public record Client(Guid BranchId, string Name, string Email) : IAggregatePayload;
