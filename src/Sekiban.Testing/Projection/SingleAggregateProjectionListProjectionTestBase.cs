using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Testing.Projection;

public class
    SingleAggregateProjectionListProjectionTestBase<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload,
        TDependencyDefinition> : CommonMultipleAggregateProjectionTestBase<
        SingleAggregateListProjector<TSingleAggregateProjection, SingleAggregateProjectionState<TAggregateProjectionPayload>,
            TSingleAggregateProjection>, SingleAggregateListProjectionState<SingleAggregateProjectionState<TAggregateProjectionPayload>>,
        TDependencyDefinition> where TAggregate : IAggregatePayload, new()
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>, new
    ()
    where TAggregateProjectionPayload : ISingleAggregateProjectionPayload
    where TDependencyDefinition : IDependencyDefinition, new()
{
    public SingleAggregateProjectionListProjectionTestBase()
    {
    }
    public SingleAggregateProjectionListProjectionTestBase(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }



    public override IMultipleAggregateProjectionTestHelper<
            SingleAggregateListProjector<TSingleAggregateProjection, SingleAggregateProjectionState<TAggregateProjectionPayload>,
                TSingleAggregateProjection>, SingleAggregateListProjectionState<SingleAggregateProjectionState<TAggregateProjectionPayload>>>
        WhenProjection()
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
            State = multipleProjectionService
                .GetSingleAggregateProjectionListObject<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>()
                .Result;
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
