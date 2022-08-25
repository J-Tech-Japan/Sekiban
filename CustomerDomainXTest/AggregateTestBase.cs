using CustomerDomainContext.Shared;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.TestHelpers;
namespace CustomerDomainXTest;

public class AggregateTestBase<TAggregate, TContents> : SingleAggregateTestBase<TAggregate, TContents>
    where TAggregate : TransferableAggregateBase<TContents>, new() where TContents : IAggregateContents, new()
{
    public AggregateTestBase() : base(CustomerDependency.GetOptions()) { }
}
