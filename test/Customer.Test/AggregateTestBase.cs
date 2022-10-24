using Customer.Domain.Shared;
using Sekiban.Core.Aggregate;
using Sekiban.Testing.SingleAggregate;
namespace Customer.Test;

public class AggregateTestBase<TAggregate, TContents> : SingleAggregateTestBase<TAggregate, TContents, CustomerDependency>
    where TAggregate : Aggregate<TContents>, new() where TContents : IAggregatePayload, new()
{
}
