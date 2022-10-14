using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Shared;
namespace Sekiban.EventSourcing.TestHelpers.ProjectionTests;

public class AggregateListProjectionTestBase<TAggregate, TAggregateContents, TDependencyDefinition> : CommonMultipleAggregateProjectionTestBase<
    SingleAggregateListProjector<TAggregate, AggregateDto<TAggregateContents>, DefaultSingleAggregateProjector<TAggregate>>,
    SingleAggregateListProjectionDto<AggregateDto<TAggregateContents>>, TDependencyDefinition>
    where TAggregate : TransferableAggregateBase<TAggregateContents>
    where TAggregateContents : IAggregateContents, new()
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
            SingleAggregateListProjector<TAggregate, AggregateDto<TAggregateContents>, DefaultSingleAggregateProjector<TAggregate>>,
            SingleAggregateListProjectionDto<AggregateDto<TAggregateContents>>> WhenProjection()
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
            Dto = multipleProjectionService.GetAggregateListObject<TAggregate, TAggregateContents>().Result;
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
