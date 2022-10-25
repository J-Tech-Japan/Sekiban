using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Testing.Projection;

public class AggregateListProjectionTestBase<TAggregatePayload, TDependencyDefinition> : CommonMultipleAggregateProjectionTestBase<
    SingleAggregateListProjector<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>, DefaultSingleAggregateProjector<TAggregatePayload>>,
    SingleAggregateListProjectionDto<AggregateState<TAggregatePayload>>, TDependencyDefinition>
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
            SingleAggregateListProjector<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>,
                DefaultSingleAggregateProjector<TAggregatePayload>>,
            SingleAggregateListProjectionDto<AggregateState<TAggregatePayload>>> WhenProjection()
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
            Dto = multipleProjectionService.GetAggregateListObject<TAggregatePayload>().Result;
        }
        catch (Exception ex)
        {
            _latestException = ex;
            return this;
        }
        ;
        foreach (var checker in _queryFilterCheckers)
        {
            checker.RegisterDto(Dto);
        }
        return this;
    }
}
