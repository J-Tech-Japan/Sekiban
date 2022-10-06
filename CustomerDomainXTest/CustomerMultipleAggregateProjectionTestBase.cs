using CustomerDomainContext.Shared;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Shared;
using Sekiban.EventSourcing.TestHelpers;
namespace CustomerDomainXTest;

public class
    CustomerMultipleAggregateProjectionTestBase<TProjection, TProjectionContents, TDependencyDefinition> : MultipleAggregateProjectionTestBase<
        TProjection, TProjectionContents, CustomerDependency> where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
    where TDependencyDefinition : IDependencyDefinition, new()
{
    protected CustomerMultipleAggregateProjectionTestBase()
    {
    }
}
