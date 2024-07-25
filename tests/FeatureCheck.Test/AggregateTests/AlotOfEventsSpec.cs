using FeatureCheck.Domain.Aggregates.ALotOfEvents;
using FeatureCheck.Domain.Aggregates.ALotOfEvents.Commands;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
using System;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class AlotOfEventsSpec : AggregateTest<ALotOfEventsAggregate, FeatureCheckDependency>
{
    [Fact]
    public void CreateTwoEventsTest()
    {
        var command = WhenCommand(new TwoEventsCreateCommand(Guid.NewGuid()));
        ThenPayloadIs(new ALotOfEventsAggregate { Count = 2 });
    }
    [Fact]
    public void Create10EventsTest()
    {
        var command = WhenCommand(new ALotOfEventsCreateCommandNext(Guid.NewGuid(), 10));
        ThenPayloadIs(new ALotOfEventsAggregate { Count = 10 });
    }
}
