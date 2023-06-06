using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Branches.Events;

public record BranchCreated(string Name) : IEventPayload<Branch, BranchCreated>
{
    public Branch OnEventInstance(Branch payload, Event<BranchCreated> ev) => OnEvent(payload, ev);
    public static Branch OnEvent(Branch payload, Event<BranchCreated> ev) => new(ev.Payload.Name);
}
