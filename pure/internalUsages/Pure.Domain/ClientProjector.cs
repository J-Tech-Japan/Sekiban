using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
namespace Pure.Domain;

public class ClientProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) =>
        (payload, ev.GetPayload()) switch
        {
            (EmptyAggregatePayload, ClientCreated created) => new Client(created.BranchId, created.Name, created.Email),
            (Client client, ClientNameChanged changed) => client with { Name = changed.Name },
            _ => payload
        };
}
