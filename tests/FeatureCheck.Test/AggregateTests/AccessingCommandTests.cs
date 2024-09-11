using FeatureCheck.Domain.Aggregates.AccessingCommands;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
using System;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class AccessingCommandTests : AggregateTest<AccessingCommand, FeatureCheckDependency>
{
    [Fact]
    public void CreateAccessingCommand()
    {
        WhenCommand(new CreateAccessingCommand());
        Assert.Equal(GetAggregateId(), GetAggregateState().Payload.AggregateId);
        Assert.NotEqual(Guid.Empty, GetAggregateState().Payload.CreatedCommandId);
    }
}
