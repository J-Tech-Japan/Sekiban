using CustomerDomainContext.Shared;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Testing.Projection;
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