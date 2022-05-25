using ESSampleProjectDependency;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.TestHelpers;
using Xunit;
namespace SampleProjectStoryXTest;

[Collection("Sequential")]
public class SPSingleAggregateTestBase<TAggregate, TDto> : SingleAggregateTestBase<TAggregate, TDto>
    where TAggregate : TransferableAggregateBase<TDto> where TDto : AggregateDtoBase
{

    public override void SetupService(IServiceCollection serviceCollection)
    {
        Dependency.RegisterForAggregateTest(serviceCollection);
    }
}
