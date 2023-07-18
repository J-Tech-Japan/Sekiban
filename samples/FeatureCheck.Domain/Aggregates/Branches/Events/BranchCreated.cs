using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Branches.Events;

public record BranchCreated(string Name) : IEventPayload<Branch, BranchCreated>
{
    public static Branch OnEvent(Branch payload, Event<BranchCreated> ev) => new(ev.Payload.Name, 0);
}
