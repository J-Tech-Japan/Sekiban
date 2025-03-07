using Orleans;
using Sekiban.Pure.Events;
namespace Pure.Domain;

[GenerateSerializer]
public record ClientCreated(Guid BranchId, string Name, string Email) : IEventPayload;
