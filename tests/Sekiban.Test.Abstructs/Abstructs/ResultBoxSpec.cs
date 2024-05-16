using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Shared;
using ResultBoxes;
using Sekiban.Testing.Story;
using Xunit.Abstractions;
namespace Sekiban.Test.Abstructs.Abstructs;

public class ResultBoxSpec : TestBase<FeatureCheckDependency>
{
    public ResultBoxSpec(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper testOutputHelper,
        ISekibanServiceProviderGenerator providerGenerator) : base(sekibanTestFixture, testOutputHelper, providerGenerator)
    {
    }

    [Fact]
    public async Task WithoutResultBoxTest()
    {
        var branchResult = await commandExecutor.ExecCommandAsync(new CreateBranch { Name = "Branch1" });
        var branchAggregateId = branchResult.AggregateId;
        if (branchAggregateId is null)
        {
            throw new Exception("AggregateId is null");
        }
        if (branchResult.EventCount == 0)
        {
            throw new Exception("Event not created");
        }
        var branchState = await aggregateLoader.AsDefaultStateAsync<Branch>(branchAggregateId.Value);
        Assert.NotNull(branchState);
        Assert.Equal("Branch1", branchState.Payload.Name);
    }


    [Fact]
    public async Task UseAndReturnWithResultBoxTest()
    {
        var branch = await commandExecutor.ExecCommandWithResultAsync(new CreateBranch { Name = "Branch1" })
            .Railway(result => result.ValidateEventCreated())
            .Railway(result => result.GetAggregateId())
            .Railway(async branchId => await aggregateLoader.AsDefaultStateWithResultAsync<Branch>(branchId));

        Assert.True(branch.IsSuccess);
        Assert.Equal("Branch1", branch.GetValue().Payload.Name);
    }
    [Fact]
    public async Task UseReturnBoxAndUnwrapBoxTest()
    {
        var branch = await commandExecutor.ExecCommandWithResultAsync(new CreateBranch { Name = "Branch1" })
            .Railway(result => result.ValidateEventCreated())
            .Railway(result => result.GetAggregateId())
            .Railway(async branchId => await aggregateLoader.AsDefaultStateWithResultAsync<Branch>(branchId))
            .UnwrapBox();

        Assert.NotNull(branch);
        Assert.Equal("Branch1", branch.Payload.Name);
    }
}
