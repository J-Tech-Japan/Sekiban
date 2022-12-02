using Customer.Domain.Shared;
using Sekiban.Core.Aggregate;
using Sekiban.Testing.SingleProjections;

namespace Customer.Test;

public class AggregateTest<TAggregatePayload> : AggregateTest<TAggregatePayload, CustomerDependency>
    where TAggregatePayload : IAggregatePayload, new()
{
}
