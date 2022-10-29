using Customer.Domain.Shared;
using Customer.WebApi;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Testing.Projection;
namespace Customer.Test;

public class
    CustomerMultipleAggregateProjectionTestBase<TProjection, TProjectionPayload, TDependencyDefinition> : MultipleAggregateProjectionTestBase<
        TProjection, TProjectionPayload, CustomerDependency> where TProjection : MultipleAggregateProjectionBase<TProjectionPayload>, new()
    where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
    where TDependencyDefinition : IDependencyDefinition, new()
{
    protected CustomerMultipleAggregateProjectionTestBase()
    {
    }
}
