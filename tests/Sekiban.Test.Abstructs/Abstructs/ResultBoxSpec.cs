using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Branches.Queries;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Queries;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using FeatureCheck.Domain.Shared;
using ResultBoxes;
using Sekiban.Testing.Story;
using Xunit.Abstractions;
namespace Sekiban.Test.Abstructs.Abstructs;

public abstract class ResultBoxSpec(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper testOutputHelper,
    ISekibanServiceProviderGenerator providerGenerator) : TestBase<FeatureCheckDependency>(
    sekibanTestFixture,
    testOutputHelper,
    providerGenerator)
{
    public string _branchName = "TESTBRANCH";
    public string _clientNameBase = "CreateClient TEST ";

    [Fact]
    public async Task WithoutResultBoxTest()
    {
        RemoveAllFromDefaultAndDissolvable();
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
        RemoveAllFromDefaultAndDissolvable();
        var branch = await sekibanExecutor
            .ExecuteCommand(new CreateBranch { Name = "Branch1" })
            .Conveyor(result => result.ValidateEventCreated())
            .Conveyor(result => result.GetAggregateId())
            .Conveyor(async branchId => await aggregateLoader.AsDefaultStateWithResultAsync<Branch>(branchId));

        Assert.True(branch.IsSuccess);
        Assert.Equal("Branch1", branch.GetValue().Payload.Name);
    }

    [Fact]
    public async Task UseAndReturnEventsWithResultBoxTest()
    {
        ;
        var branch = await RemoveAllFromDefaultAndDissolvableWithResultBox()
            .Conveyor(async _ => await sekibanExecutor.ExecuteCommand(new CreateBranch { Name = "Branch1" }))
            .Conveyor(result => result.ValidateEventCreated())
            .Conveyor(result => result.GetAggregateId())
            .Conveyor(async branchId => await aggregateLoader.AsDefaultStateWithResultAsync<Branch>(branchId));

        Assert.True(branch.IsSuccess);
        Assert.Equal("Branch1", branch.GetValue().Payload.Name);
    }

    [Fact]
    public async Task UseReturnBoxAndUnwrapBoxTest()
    {
        var branch = await RemoveAllFromDefaultAndDissolvableWithResultBox()
            .Conveyor(async _ => await sekibanExecutor.ExecuteCommand(new CreateBranch { Name = "Branch1" }))
            .Conveyor(result => result.ValidateEventCreated())
            .Conveyor(result => result.GetAggregateId())
            .Conveyor(async branchId => await aggregateLoader.AsDefaultStateWithResultAsync<Branch>(branchId))
            .UnwrapBox();

        Assert.NotNull(branch);
        Assert.Equal("Branch1", branch.Payload.Name);
    }

    [Fact]
    public async Task UseReturnBoxAndUnwrapWithoutValidationBoxTest()
    {
        RemoveAllFromDefaultAndDissolvable();
        var branch = await sekibanExecutor
            .ExecuteCommandWithoutValidation(new CreateBranch { Name = "Branch1" })
            .Conveyor(result => result.ValidateEventCreated())
            .Conveyor(result => result.GetAggregateId())
            .Conveyor(async branchId => await aggregateLoader.AsDefaultStateWithResultAsync<Branch>(branchId))
            .UnwrapBox();

        Assert.NotNull(branch);
        Assert.Equal("Branch1", branch.Payload.Name);
    }
    [Fact]
    public async Task UseReturnBoxAndUnwrapWithoutValidationWithEventsBoxTest()
    {
        RemoveAllFromDefaultAndDissolvable();
        var branch = await sekibanExecutor
            .ExecuteCommandWithoutValidationWithEvents(new CreateBranch { Name = "Branch1" })
            .Conveyor(result => result.ValidateEventCreated())
            .Conveyor(result => result.GetAggregateId())
            .Conveyor(async branchId => await sekibanExecutor.GetAggregateState<Branch>(branchId))
            .UnwrapBox();

        Assert.NotNull(branch);
        Assert.Equal("Branch1", branch.Payload.Name);
    }
    [Fact]
    public async Task UseReturnBoxQueryTest()
    {
        var result = await RemoveAllFromDefaultAndDissolvableWithResultBox()
            .Conveyor(async _ => await sekibanExecutor.ExecuteCommand(new CreateBranch { Name = "Branch 22" }))
            .Conveyor(
                response => response.AggregateId is not null
                    ? ResultBox<Guid>.FromValue(response.AggregateId.Value)
                    : new ApplicationException("AggregateId is null"))
            .Conveyor(async branchId => await sekibanExecutor.ExecuteQuery(new BranchExistsQuery.Parameter(branchId)))
            .UnwrapBox();

        Assert.True(result.Exists);
    }

    [Fact]
    public async Task UseReturnBoxListQueryTest()
    {
        var result = await RemoveAllFromDefaultAndDissolvableWithResultBox()
            .Conveyor(async _ => await sekibanExecutor.ExecuteCommand(new CreateBranch { Name = "Branch 23" }))
            .Conveyor(
                response => response switch
                {
                    { AggregateId: not null } => ResultBox.FromValue(BranchId.FromValue(response.AggregateId.Value)),
                    _ => new ApplicationException("AggregateId is null")
                })
            .Combine(
                async branchId =>
                    await sekibanExecutor.ExecuteCommand(
                        new CreateClient(branchId.Value, "Client1", "client1@example.com")))
            .Conveyor(
                twoValues => twoValues.Value2.AggregateId is not null
                    ? ResultBox.FromValue(TwoValues.FromValues(twoValues.Value1, twoValues.Value2.AggregateId.Value))
                    : new ApplicationException("AggregateId is null"))
            .Conveyor(twoValues => sekibanExecutor.ExecuteQuery(new GetClientPayloadQuery.Parameter("Cl")))
            .UnwrapBox();
        Assert.Single(result.Items);
        Assert.Equal("Client1", result.Items.First().Client.ClientName);
    }
    [Fact]
    public async Task NextQueryTest() =>
        await ResultBox
            .WrapTry(RemoveAllFromDefaultAndDissolvableWithResultBox)
            .Conveyor(_ => sekibanExecutor.ExecuteCommand(new CreateBranch { Name = "Branch 24" }))
            .Conveyor(response => response.GetAggregateId().Remap(BranchId.FromValue))
            .Conveyor(
                branchId => sekibanExecutor.ExecuteCommand(
                    new CreateClientR(branchId.Value, "Client1", "test@example.com")))
            .Conveyor(_ => sekibanExecutor.ExecuteQuery(new ClientEmailExistQueryNext("test@example.com")))
            .Do(Assert.True)
            .Combine(_ => sekibanExecutor.ExecuteQuery(new ClientEmailExistQueryNextAsync("test@example.com")))
            .Do(Assert.Equal)
            .Conveyor(_ => sekibanExecutor.ExecuteQuery(new ClientEmailExistQueryNext("test@examplesssss.com")))
            .Do(Assert.False)
            .Combine(_ => sekibanExecutor.ExecuteQuery(new ClientEmailExistQueryNextAsync("test@examplesssss.com")))
            .Scan(Assert.Equal)
            .ScanResult(result => Assert.True(result.IsSuccess));
    [Fact]
    public async Task CheckValidationError() =>
        await sekibanExecutor
            .ExecuteCommand(new CreateBranch { Name = string.Empty })
            .DoResult(
                result =>
                {
                    Assert.True(result.IsSuccess);
                    Assert.NotNull(result.GetValue().ValidationResults);
                });

    [Fact]
    public async Task CheckValidationErrorWithHandler() =>
        await sekibanExecutor
            .ExecuteCommand(new CreateBranchWithResult(string.Empty))
            .DoResult(
                result =>
                {
                    Assert.True(result.IsSuccess);
                    Assert.NotNull(result.GetValue().ValidationResults);
                });



    [Fact]
    public Task NextQueryTest2() =>
        ResultBox
            .WrapTry(RemoveAllFromDefaultAndDissolvableWithResultBox)
            .Conveyor(_ => sekibanExecutor.ExecuteCommand(new CreateBranch { Name = "Branch 24" }))
            .Conveyor(response => response.GetAggregateId().Remap(BranchId.FromValue))
            .Conveyor(
                branchId => sekibanExecutor.ExecuteCommand(
                    new CreateClient(branchId.Value, "Client1", "test@example.com")))
            .Conveyor(_ => sekibanExecutor.ExecuteQuery(new GetClientPayloadQueryNext("Client1")))
            .Combine(_ => sekibanExecutor.ExecuteQuery(new GetClientPayloadQuery.Parameter("Client1")))
            .Scan(Assert.Equal)
            .Conveyor(_ => sekibanExecutor.ExecuteQuery(new GetClientPayloadQueryNextAsync("Client1")))
            .Combine(_ => sekibanExecutor.ExecuteQuery(new GetClientPayloadQuery.Parameter("Client1")))
            .Scan(Assert.Equal)
            .Conveyor(_ => sekibanExecutor.ExecuteQuery(new GetClientPayloadQueryNext("Cli---ent1")))
            .Combine(_ => sekibanExecutor.ExecuteQuery(new GetClientPayloadQuery.Parameter("Cli---ent1")))
            .Scan(Assert.Equal)
            .Conveyor(_ => sekibanExecutor.ExecuteQuery(new GetClientPayloadQueryNextAsync("Cli---ent1")))
            .Combine(_ => sekibanExecutor.ExecuteQuery(new GetClientPayloadQuery.Parameter("Cli---ent1")))
            .Scan(Assert.Equal);


    [Fact]
    public Task NextQueryTestWithProjection() =>
        ResultBox
            .WrapTry(RemoveAllFromDefaultAndDissolvableWithResultBox)
            .Conveyor(_ => sekibanExecutor.ExecuteCommand(new CreateBranch(_branchName)))
            .Conveyor(response => response.GetAggregateId().Remap(BranchId.FromValue))
            .Do(
                branchId => sekibanExecutor.ExecuteCommand(
                    new CreateClient(branchId.Value, _clientNameBase + 1, "test" + 1 + "@example.com")))
            .Do(
                branchId => sekibanExecutor.ExecuteCommand(
                    new CreateClient(branchId.Value, _clientNameBase + 2, "test" + 2 + "@example.com")))
            .Do(
                branchId => sekibanExecutor.ExecuteCommand(
                    new CreateClient(branchId.Value, _clientNameBase + 3, "test" + 3 + "@example.com")))
            .Do(
                branchId => sekibanExecutor.ExecuteCommand(
                    new CreateClient(branchId.Value, _clientNameBase + 4, "test" + 4 + "@example.com")))
            .Do(
                branchId => sekibanExecutor.ExecuteCommand(
                    new CreateClient(branchId.Value, _clientNameBase + 5, "test" + 5 + "@example.com")))
            .Conveyor(
                _ => sekibanExecutor.ExecuteQuery(
                    new ClientLoyaltyPointQueryNext(null, null, 3, 1, null, null, null, null)))
            .Combine(
                _ => sekibanExecutor.ExecuteQuery(
                    new ClientLoyaltyPointQuery.Parameter(null, null, 3, 1, null, null, null, null)))
            .Do(Assert.Equal);
}
