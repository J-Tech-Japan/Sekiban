using FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
using Xunit;
namespace FeatureCheck.Test.AggregateTests.Subtypes;

public class InheritedAggregateTest : AggregateTest<IInheritedAggregate, FeatureCheckDependency>
{
    [Fact]
    public void Test()
    {
        Subtype<ProcessingSubAggregate>()
            .WhenCommand(
                new OpenInheritedAggregate
                {
                    YearMonth = 202201
                });
        ThenPayloadTypeShouldBe<ProcessingSubAggregate>()
            .WhenCommand(
                new CloseInheritedAggregate
                    { Reason = "test", AggregateId = GetAggregateId() });
        ThenPayloadTypeShouldBe<ClosedSubAggregate>()
            .WhenCommand(
                new ReopenInheritedAggregate
                    { Reason = "test", AggregateId = GetAggregateId() });
        ThenPayloadTypeShouldBe<ProcessingSubAggregate>()
            .WhenCommand(
                new CloseInheritedAggregate
                    { Reason = "test", AggregateId = GetAggregateId() });
    }
}