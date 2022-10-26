using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.MultipleAggregate;
namespace Sekiban.Testing.Projection;

public class
    MultipleAggregateProjectionTestBase<TProjection, TProjectionPayload, TDependencyDefinition> : CommonMultipleAggregateProjectionTestBase<
        TProjection, TProjectionPayload, TDependencyDefinition> where TProjection : MultipleAggregateProjectionBase<TProjectionPayload>, new()
    where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
    where TDependencyDefinition : IDependencyDefinition, new()
{
    public MultipleAggregateProjectionTestBase()
    {
    }
    public MultipleAggregateProjectionTestBase(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    public sealed override IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> WhenProjection()
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("Service provider not set");
        }
        var multipleProjectionService
            = _serviceProvider.GetRequiredService(typeof(IMultipleAggregateProjectionService)) as IMultipleAggregateProjectionService;
        if (multipleProjectionService is null) { throw new Exception("Failed to get multipleProjectionService "); }
        try
        {
            State = multipleProjectionService.GetProjectionAsync<TProjection, TProjectionPayload>().Result;
        }
        catch (Exception ex)
        {
            _latestException = ex;
            return this;
        }
        ;
        foreach (var checker in _queryFilterCheckers)
        {
            checker.RegisterState(State);
        }
        return this;
    }
}
