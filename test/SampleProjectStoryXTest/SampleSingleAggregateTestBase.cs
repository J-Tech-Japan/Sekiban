using ESSampleProjectDependency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.TestHelpers;
using System;
using Xunit;
namespace SampleProjectStoryXTest;

[Collection("Sequential")]
public class SampleSingleAggregateTestBase<TAggregate, TContents> : SingleAggregateTestBase<TAggregate, TContents>
    where TAggregate : TransferableAggregateBase<TContents>, new() where TContents : IAggregateContents, new()
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
