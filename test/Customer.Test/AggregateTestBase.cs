using Customer.Domain.Shared;
using Sekiban.Core.Aggregate;
using Sekiban.Testing.SingleAggregate;
namespace Customer.Test;

public class AggregateTestBase<TAggregatePayload> : SingleAggregateTestBase<TAggregatePayload, CustomerDependency>
    where TAggregatePayload : IAggregatePayload, new()
{
}
