using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Testing.Projection;

public class
    SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TAggregateProjectionPayload,
        TDependencyDefinition> : CommonMultiProjectionTestBase<
        SingleProjectionListProjector<TSingleProjection, SingleProjectionState<TAggregateProjectionPayload>,
            TSingleProjection>, SingleProjectionListState<SingleProjectionState<TAggregateProjectionPayload>>,
        TDependencyDefinition> where TAggregatePayload : IAggregatePayload, new()
    where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TAggregateProjectionPayload>, new
    ()
    where TAggregateProjectionPayload : ISingleProjectionPayload
    where TDependencyDefinition : IDependencyDefinition, new()
{
    public SingleProjectionListTestBase()
    {
    }
    public SingleProjectionListTestBase(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }



    public override IMultiProjectionTestHelper<
            SingleProjectionListProjector<TSingleProjection, SingleProjectionState<TAggregateProjectionPayload>,
                TSingleProjection>, SingleProjectionListState<SingleProjectionState<TAggregateProjectionPayload>>>
        WhenProjection()
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
            State = multipleProjectionService
                .GetSingleProjectionListObject<TAggregatePayload, TSingleProjection, TAggregateProjectionPayload>()
                .Result;
        }
        catch (Exception ex)
        {
            _latestException = ex;
            return this;
        }
        ;
        foreach (var checker in _queryCheckers)
        {
            checker.RegisterState(State);
        }
        return this;
    }
}
