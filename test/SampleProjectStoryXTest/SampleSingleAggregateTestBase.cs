using ESSampleProjectDependency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.TestHelpers;
using System;
using Xunit;
namespace SampleProjectStoryXTest;

[Collection("Sequential")]
public class SampleSingleAggregateTestBase<TAggregate, TDto> : SingleAggregateTestBase<TAggregate, TDto>
    where TAggregate : TransferableAggregateBase<TDto> where TDto : AggregateDtoBase
{

    public override IServiceProvider SetupService()
    {
        var testFixture = new TestFixture();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(testFixture.Configuration);
        Dependency.RegisterForAggregateTest(services);
        return services.BuildServiceProvider();
    }
}
