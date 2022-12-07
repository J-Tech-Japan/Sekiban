using Sekiban.Core.Event;
namespace FeatureCheck.Domain.Aggregates.Branches.Events;

public record BranchCreated(string Name) : IEventPayload<Branch>
{
    public Branch OnEvent(Branch payload, IEvent ev)
    {
        return new(Name);
    }
}
