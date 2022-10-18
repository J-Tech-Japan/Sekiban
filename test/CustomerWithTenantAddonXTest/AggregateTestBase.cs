using CustomerWithTenantAddonDomainContext.Shared;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.TestHelpers.SingleAggregates;
namespace CustomerWithTenantAddonXTest;

public class AggregateTestBase<TAggregate, TContents> : SingleAggregateTestBase<TAggregate, TContents, CustomerWithTenantAddonDependency>
    where TAggregate : AggregateBase<TContents>, new() where TContents : IAggregateContents, new()
{
}
