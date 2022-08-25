using CustomerWithTenantAddonDomainContext.Shared;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.TestHelpers;
namespace CustomerWithTenantAddonXTest;

public class AggregateTestBase<TAggregate, TContents> : SingleAggregateTestBase<TAggregate, TContents>
    where TAggregate : TransferableAggregateBase<TContents>, new() where TContents : IAggregateContents, new()
{

    public AggregateTestBase() : base(CustomerWithTenantAddonDependency.GetOptions()) { }
}
