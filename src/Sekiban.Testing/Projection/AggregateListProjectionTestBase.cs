using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Testing.Projection;

public class AggregateListProjectionTestBase<TAggregatePayload, TDependencyDefinition> : CommonMultipleAggregateProjectionTestBase<
    SingleProjectionListProjector<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>, DefaultSingleProjector<TAggregatePayload>>,
    SingleProjectionListState<AggregateState<TAggregatePayload>>, TDependencyDefinition>
    where TAggregatePayload : IAggregatePayload, new()
    where TDependencyDefinition : IDependencyDefinition, new()
{
    public AggregateListProjectionTestBase()
    {
    }
    public AggregateListProjectionTestBase(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }
    public override
        IMultipleAggregateProjectionTestHelper<
            SingleProjectionListProjector<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>,
                DefaultSingleProjector<TAggregatePayload>>,
            SingleProjectionListState<AggregateState<TAggregatePayload>>> WhenProjection()
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("Service provider not set");
        }
        var multipleProjectionService
            = _serviceProvider.GetRequiredService(typeof(IMultiProjectionService)) as IMultiProjectionService;
        if (multipleProjectionService is null) { throw new Exception("Failed to get multipleProjectionService "); }
        try
        {
            State = multipleProjectionService.GetAggregateListObject<TAggregatePayload>().Result;
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
