using FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes;
using FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Commands;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
using Xunit;
namespace FeatureCheck.Test.AggregateTests.Subtypes;

public class BaseFirstAggregateTests : AggregateTest<BaseFirstAggregate, FeatureCheckDependency>
{
    [Fact]
    public void CreateSpec()
    {
        WhenCommand(new BFAggregateCreateAccount("test", 100));

        ThenPayloadIs(new BaseFirstAggregate { Name = "test", Price = 100 });
        ThenPayloadTypeShouldBe<BaseFirstAggregate>();
    }

    [Fact]
    public void ActivateSpec()
    {
        // Given
        GivenCommand(new BFAggregateCreateAccount("test", 100));

        // When
        WhenCommand(new ActivateBFAggregate(GetAggregateId()));

        // Then
        ThenPayloadIs(new ActiveBFAggregate { Name = "test", Price = 100 });
    }

    [Fact]
    public void CloseSpec()
    {
        // Given
        GivenCommand(new BFAggregateCreateAccount("test", 100));
        GivenCommand(new ActivateBFAggregate(GetAggregateId()));

        // When
        WhenSubtypeCommand(new CloseBFAggregate(GetAggregateId()));

        // Then
        ThenPayloadIs(new ClosedBFAggregate { Name = "test", Price = 100 });
    }

    [Fact]
    public void ReopenSpec()
    {
        // Given
        GivenCommand(new BFAggregateCreateAccount("test", 100));
        GivenCommand(new ActivateBFAggregate(GetAggregateId()))
            .ThenPayloadTypeShouldBe<ActiveBFAggregate>()
            .GivenCommand(new CloseBFAggregate(GetAggregateId()))

            // When
            .ThenPayloadTypeShouldBe<ClosedBFAggregate>()
            .WhenCommand(new ReopenBFAggregate(GetAggregateId()))

            // Then
            .ThenPayloadTypeShouldBe<BaseFirstAggregate>()
            .ThenPayloadIs(new BaseFirstAggregate { Name = "test", Price = 100 });
    }
    [Fact]
    public void ReopenSpecSimple()
    {
        // Given
        GivenCommand(new BFAggregateCreateAccount("test", 100));
        GivenCommand(new ActivateBFAggregate(GetAggregateId()));
        GivenSubtypeCommand(new CloseBFAggregate(GetAggregateId()));

        // When
        WhenSubtypeCommand(new ReopenBFAggregate(GetAggregateId()));

        // Then
        ThenPayloadIs(new BaseFirstAggregate { Name = "test", Price = 100 });
    }
}
