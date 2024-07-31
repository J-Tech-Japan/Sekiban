using FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes;
using FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Commands;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
using Xunit;
namespace FeatureCheck.Test.AggregateTests.Subtypes;

public class InheritInSubtypesTest : AggregateTest<IInheritInSubtypesType, FeatureCheckDependency>
{
    [Fact]
    public void InheritInSubtypesChangeStageSpec()
    {
        Subtype<FirstStage>()
            .GivenCommand(new CreateInheritInSubtypesType(1))
            .WhenCommand(new ChangeToSecond(GetAggregateId(), 2));
        ThenPayloadTypeShouldBe<SecondStage>();
    }

    [Fact]
    public void InheritInSubtypesChangeBackStageSpec()
    {
        Subtype<FirstStage>()
            .GivenCommand(new CreateInheritInSubtypesType(1))
            .GivenCommand(new ChangeToSecond(GetAggregateId(), 2));
        ThenPayloadTypeShouldBe<SecondStage>()
            .WhenCommand(new MoveBackToFirst(GetAggregateId()));
        ThenPayloadTypeShouldBe<FirstStage>();
    }

    [Fact]
    public void InheritInSubtypesChangeBackStageSpec2()
    {
        Subtype<FirstStage>()
            .GivenCommand(new CreateInheritInSubtypesType(1))
            .GivenCommand(new ChangeToSecond(GetAggregateId(), 2));
        ThenPayloadTypeShouldBe<SecondStage>()
            .GivenCommand(new MoveBackToFirst(GetAggregateId()));
        ThenPayloadTypeShouldBe<FirstStage>()
            .WhenCommand(new ChangeToSecond(GetAggregateId(), 3));
        ThenPayloadTypeShouldBe<SecondStage>();
    }
}
