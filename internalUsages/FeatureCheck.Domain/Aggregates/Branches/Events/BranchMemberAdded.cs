using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Branches.Events;

public record BranchMemberAdded(Guid ClientId) : IEventPayload<Branch, BranchMemberAdded>
{
    public static Branch OnEvent(Branch aggregatePayload, Event<BranchMemberAdded> ev) => aggregatePayload with
    {
        NumberOfMembers = aggregatePayload.NumberOfMembers + 1
    };
}
