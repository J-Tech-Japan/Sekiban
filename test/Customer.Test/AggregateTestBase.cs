using Customer.Domain.Shared;
using Sekiban.Core.Aggregate;
using Sekiban.Testing.SingleProjections;
namespace Customer.Test;

public class AggregateTestBase<TAggregatePayload> : AggregateTestBase<TAggregatePayload, CustomerDependency>
    where TAggregatePayload : IAggregatePayload, new()
{
}
