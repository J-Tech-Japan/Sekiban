using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Branches.Events;

public record BranchCreated(string Name) : ICreatedEvent<Branch>
{
    public Branch OnEvent(Branch payload, IEvent @event) => new Branch(Name);
}
