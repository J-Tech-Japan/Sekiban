using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Branches.Events;

public record BranchCreated(string Name) : IEventPayload<Branch>
{
    public Branch OnEvent(Branch payload, IEvent ev) => new Branch(Name);
}
