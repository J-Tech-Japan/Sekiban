using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.TestHelpers;

public class AggregateListProjectionTestBase<TAggregate, TAggregateContents> : CommonMultipleAggregateProjectionTestBase<
    SingleAggregateListProjector<TAggregate, AggregateDto<TAggregateContents>, DefaultSingleAggregateProjector<TAggregate>>,
    SingleAggregateListProjectionDto<AggregateDto<TAggregateContents>>> where TAggregate : TransferableAggregateBase<TAggregateContents>
    where TAggregateContents : IAggregateContents, new()
{
    public AggregateListProjectionTestBase(SekibanDependencyOptions dependencyOptions) : base(dependencyOptions)
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
