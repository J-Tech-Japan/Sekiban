using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.SingleAggregate;
using System;
namespace Sekiban.Testing.Projection;

public class AggregateListProjectionTestBase<TAggregate, TAggregateContents, TDependencyDefinition> : CommonMultipleAggregateProjectionTestBase<
    SingleAggregateListProjector<TAggregate, AggregateDto<TAggregateContents>, DefaultSingleAggregateProjector<TAggregate>>,
    SingleAggregateListProjectionDto<AggregateDto<TAggregateContents>>, TDependencyDefinition> where TAggregate : AggregateBase<TAggregateContents>
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