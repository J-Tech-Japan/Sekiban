using CustomerWithTenantAddonDomainContext.Shared;
using Sekiban.Core.Aggregate;
using Sekiban.Testing.SingleAggregate;
namespace CustomerWithTenantAddonXTest;

public class AggregateTestBase<TAggregate, TContents> : SingleAggregateTestBase<TAggregate, TContents, CustomerWithTenantAddonDependency>
    where TAggregate : AggregateBase<TContents>, new() where TContents : IAggregateContents, new()
{
}
