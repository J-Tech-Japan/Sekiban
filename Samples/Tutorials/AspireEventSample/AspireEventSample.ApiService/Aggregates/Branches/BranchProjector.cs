using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;

namespace AspireEventSample.ApiService.Aggregates.Branches;

public class BranchProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) =>
        (payload, ev.GetPayload()) switch
        {
            (EmptyAggregatePayload, BranchCreated created) => new Branch(created.Name, created.Country),
            (Branch branch, BranchNameChanged changed) => new Branch(changed.Name, branch.Country),
            _ => payload
        };
}
