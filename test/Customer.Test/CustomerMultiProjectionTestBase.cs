using Customer.Domain.Shared;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Testing.Projection;
namespace Customer.Test;

public class
    CustomerMultiProjectionTestBase<TProjection, TProjectionPayload, TDependencyDefinition> : MultiProjectionTestBase<
        TProjection, TProjectionPayload, CustomerDependency> where TProjection : MultiProjectionBase<TProjectionPayload>, new()
    where TProjectionPayload : IMultiProjectionPayload, new()
    where TDependencyDefinition : IDependencyDefinition, new()
{
    protected CustomerMultiProjectionTestBase()
    {
    }
}
