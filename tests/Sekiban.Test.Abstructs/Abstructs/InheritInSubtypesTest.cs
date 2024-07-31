using FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes;
using FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Commands;
using FeatureCheck.Domain.Shared;
using ResultBoxes;
using Sekiban.Core.Exceptions;
using Sekiban.Testing.Story;
using Xunit.Abstractions;
namespace Sekiban.Test.Abstructs.Abstructs;

public abstract class InheritInSubtypesTest(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper output,
    ISekibanServiceProviderGenerator providerGenerator)
    : TestBase<FeatureCheckDependency>(sekibanTestFixture, output, providerGenerator)
{
    [Fact]
    public async Task ThrowsIfSubtypeIsNotCurrentStateSpec()
    {
        RemoveAllFromDefaultAndDissolvable();

        var result = await sekibanExecutor
            .ExecuteCommand(new CreateInheritInSubtypesType(1))
            .Conveyor(response => ResultBox.CheckNull(response.AggregateId))
            .Conveyor(aggregateId => sekibanExecutor.ExecuteCommand(new MoveBackToFirst(aggregateId)));
        Assert.False(result.IsSuccess);
        Assert.IsType<AggregateTypeNotMatchException>(result.GetException());
    }
    [Fact]
    public async Task ThrowsIfSubtypeIsNotCurrentStateYieldSpec()
    {
        RemoveAllFromDefaultAndDissolvable();

        var result = await sekibanExecutor
            .ExecuteCommand(new CreateInheritInSubtypesType(1))
            .Conveyor(response => ResultBox.CheckNull(response.AggregateId))
            .Conveyor(aggregateId => sekibanExecutor.ExecuteCommand(new MoveBackToFirstYield(aggregateId)));
        Assert.False(result.IsSuccess);
        Assert.IsType<AggregateTypeNotMatchException>(result.GetException());
    }
    [Fact]
    public async Task SuccessWithRegularSpec()
    {
        RemoveAllFromDefaultAndDissolvable();

        var result = await sekibanExecutor
            .ExecuteCommand(new CreateInheritInSubtypesType(1))
            .Conveyor(response => ResultBox.CheckNull(response.AggregateId))
            .Do(aggregateId => sekibanExecutor.ExecuteCommand(new ChangeToSecond(aggregateId, 2)))
            .Do(aggregateId => sekibanExecutor.ExecuteCommand(new MoveBackToFirst(aggregateId)));
        Assert.True(result.IsSuccess);

        var aggregateState = await sekibanExecutor.GetAggregateState<FirstStage>(result.GetValue());
        Assert.True(aggregateState.IsSuccess);
        Assert.Equal(nameof(FirstStage), aggregateState.GetValue().PayloadTypeName);
    }
    [Fact]
    public async Task SuccessWithRegularYieldSpec()
    {
        RemoveAllFromDefaultAndDissolvable();

        var result = await sekibanExecutor
            .ExecuteCommand(new CreateInheritInSubtypesType(1))
            .Conveyor(response => ResultBox.CheckNull(response.AggregateId))
            .Do(aggregateId => sekibanExecutor.ExecuteCommand(new ChangeToSecondYield(aggregateId, 2)))
            .Do(aggregateId => sekibanExecutor.ExecuteCommand(new MoveBackToFirstYield(aggregateId)));
        Assert.True(result.IsSuccess);

        var aggregateState = await sekibanExecutor.GetAggregateState<FirstStage>(result.GetValue());
        Assert.True(aggregateState.IsSuccess);
        Assert.Equal(nameof(FirstStage), aggregateState.GetValue().PayloadTypeName);
    }
    [Fact]
    public async Task ThrowsIfSubtypeIsNotCurrentState2Spec()
    {
        RemoveAllFromDefaultAndDissolvable();

        var result = await sekibanExecutor
            .ExecuteCommand(new CreateInheritInSubtypesType(1))
            .Conveyor(response => ResultBox.CheckNull(response.AggregateId))
            .Do(aggregateId => sekibanExecutor.ExecuteCommand(new ChangeToSecond(aggregateId, 2)))
            .Conveyor(aggregateId => sekibanExecutor.ExecuteCommand(new ChangeToSecond(aggregateId, 2)));
        Assert.False(result.IsSuccess);
        Assert.IsType<AggregateTypeNotMatchException>(result.GetException());
    }
    [Fact]
    public async Task ThrowsIfSubtypeIsNotCurrentStateYield2Spec()
    {
        RemoveAllFromDefaultAndDissolvable();

        var result = await sekibanExecutor
            .ExecuteCommand(new CreateInheritInSubtypesType(1))
            .Conveyor(response => ResultBox.CheckNull(response.AggregateId))
            .Do(aggregateId => sekibanExecutor.ExecuteCommand(new ChangeToSecondYield(aggregateId, 2)))
            .Conveyor(aggregateId => sekibanExecutor.ExecuteCommand(new ChangeToSecondYield(aggregateId, 2)));
        Assert.False(result.IsSuccess);
        Assert.IsType<AggregateTypeNotMatchException>(result.GetException());
    }
}
