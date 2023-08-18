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
        WhenCommand(new BFAggregateCreateAccount("test", 100));

        // When
        WhenCommand(new ActivateBFAggregate(GetAggregateId()))

            // Then
            .ThenPayloadTypeShouldBe<ActiveBFAggregate>()
            .ThenPayloadIs(new ActiveBFAggregate { Name = "test", Price = 100 });
    }

    [Fact]
    public void CloseSpec()
    {
        // Given
        WhenCommand(new BFAggregateCreateAccount("test", 100));
        WhenCommand(new ActivateBFAggregate(GetAggregateId()))
            .ThenPayloadTypeShouldBe<ActiveBFAggregate>()

            // When
            .WhenCommand(new CloseBFAggregate(GetAggregateId()))

            // Then
            .ThenPayloadTypeShouldBe<ClosedBFAggregate>()
            .ThenPayloadIs(new ClosedBFAggregate { Name = "test", Price = 100 });
    }

    [Fact]
    public void ReopenSpec()
    {
        // Given
        WhenCommand(new BFAggregateCreateAccount("test", 100));
        WhenCommand(new ActivateBFAggregate(GetAggregateId()))
            .ThenPayloadTypeShouldBe<ActiveBFAggregate>()
            .WhenCommand(new CloseBFAggregate(GetAggregateId()))

            // When
            .ThenPayloadTypeShouldBe<ClosedBFAggregate>()
            .WhenCommand(new ReopenBFAggregate(GetAggregateId()))

            // Then
            .ThenPayloadTypeShouldBe<BaseFirstAggregate>()
            .ThenPayloadIs(new BaseFirstAggregate { Name = "test", Price = 100 });
    }
}
