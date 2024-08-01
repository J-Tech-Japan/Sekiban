using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Branches.Events;

public record BranchNameChanged(string Name) : IEventPayload<Branch, BranchNameChanged>
{
    public static Branch OnEvent(Branch aggregatePayload, Event<BranchNameChanged> ev) => aggregatePayload with
    {
        Name = ev.Payload.Name
    };
}
