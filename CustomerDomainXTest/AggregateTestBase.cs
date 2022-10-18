using CustomerDomainContext.Shared;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.TestHelpers.SingleAggregates;
namespace CustomerDomainXTest;

public class AggregateTestBase<TAggregate, TContents> : SingleAggregateTestBase<TAggregate, TContents, CustomerDependency>
    where TAggregate : AggregateBase<TContents>, new() where TContents : IAggregateContents, new()
{
}
