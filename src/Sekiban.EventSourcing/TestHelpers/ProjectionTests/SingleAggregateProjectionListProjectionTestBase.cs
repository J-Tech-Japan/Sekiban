using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Shared;
namespace Sekiban.EventSourcing.TestHelpers.ProjectionTests;

public class
    SingleAggregateProjectionListProjectionTestBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents,
        TDependencyDefinition> : CommonMultipleAggregateProjectionTestBase<
        SingleAggregateListProjector<TSingleAggregateProjection, SingleAggregateProjectionDto<TSingleAggregateProjectionContents>,
            TSingleAggregateProjection>, SingleAggregateListProjectionDto<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>,
        TDependencyDefinition> where TAggregate : AggregateCommonBase, new()
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>, new
    ()
    where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
    where TDependencyDefinition : IDependencyDefinition, new()
{
    public SingleAggregateProjectionListProjectionTestBase()
    {
    }
    public SingleAggregateProjectionListProjectionTestBase(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }



    public override IMultipleAggregateProjectionTestHelper<
            SingleAggregateListProjector<TSingleAggregateProjection, SingleAggregateProjectionDto<TSingleAggregateProjectionContents>,
                TSingleAggregateProjection>, SingleAggregateListProjectionDto<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>>>
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
            Dto = multipleProjectionService
                .GetSingleAggregateProjectionListObject<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>()
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
            checker.RegisterDto(Dto);
        }
        return this;
    }
}
