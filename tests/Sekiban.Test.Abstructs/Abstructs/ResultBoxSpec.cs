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

public abstract class ResultBoxSpec : TestBase<FeatureCheckDependency>
{

    public string _branchName = "TESTBRANCH";

    public Guid _clientId1 = Guid.NewGuid();
    public Guid _clientId2 = Guid.NewGuid();
    public Guid _clientId3 = Guid.NewGuid();
    public Guid _clientId4 = Guid.NewGuid();
    public Guid _clientId5 = Guid.NewGuid();
    public string _clientNameBase = "CreateClient TEST ";
    public ResultBoxSpec(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper testOutputHelper,
        ISekibanServiceProviderGenerator providerGenerator) : base(sekibanTestFixture, testOutputHelper, providerGenerator)
    {
    }

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
        var branch = await commandExecutor.ExecCommandWithResultAsync(new CreateBranch { Name = "Branch1" })
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
            .Conveyor(async _ => await commandExecutor.ExecCommandWithEventsWithResultAsync(new CreateBranch { Name = "Branch1" }))
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
            .Conveyor(async _ => await commandExecutor.ExecCommandWithResultAsync(new CreateBranch { Name = "Branch1" }))
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
        var branch = await commandExecutor.ExecCommandWithoutValidationWithResultAsync(new CreateBranch { Name = "Branch1" })
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
        var branch = await commandExecutor.ExecCommandWithoutValidationWithEventsWithResultAsync(new CreateBranch { Name = "Branch1" })
            .Conveyor(result => result.ValidateEventCreated())
            .Conveyor(result => result.GetAggregateId())
            .Conveyor(async branchId => await aggregateLoader.AsDefaultStateWithResultAsync<Branch>(branchId))
            .UnwrapBox();

        Assert.NotNull(branch);
        Assert.Equal("Branch1", branch.Payload.Name);
    }
    [Fact]
    public async Task UseReturnBoxQueryTest()
    {
        var result = await RemoveAllFromDefaultAndDissolvableWithResultBox()
            .Conveyor(async _ => await commandExecutor.ExecCommandWithResultAsync(new CreateBranch { Name = "Branch 22" }))
            .Conveyor(
                response => response.AggregateId is not null
                    ? ResultBox<Guid>.FromValue(response.AggregateId.Value)
                    : new ApplicationException("AggregateId is null"))
            .Conveyor(async branchId => await queryExecutor.ExecuteWithResultAsync(new BranchExistsQuery.Parameter(branchId)))
            .UnwrapBox();

        Assert.True(result.Exists);
    }

    [Fact]
    public async Task UseReturnBoxListQueryTest()
    {
        var result = await RemoveAllFromDefaultAndDissolvableWithResultBox()
            .Conveyor(async _ => await commandExecutor.ExecCommandWithResultAsync(new CreateBranch { Name = "Branch 23" }))
            .Conveyor(
                response => response switch
                {
                    { AggregateId: not null } => ResultBox.FromValue(BranchId.FromValue(response.AggregateId.Value)),
                    _ => new ApplicationException("AggregateId is null")
                })
            .Combine(
                async branchId =>
                    await commandExecutor.ExecCommandWithResultAsync(new CreateClient(branchId.Value, "Client1", "client1@example.com")))
            .Conveyor(
                twoValues => twoValues.Value2.AggregateId is not null
                    ? ResultBox.FromValue(TwoValues.FromValues(twoValues.Value1, twoValues.Value2.AggregateId.Value))
                    : new ApplicationException("AggregateId is null"))
            .Conveyor(twoValues => queryExecutor.ExecuteWithResultAsync(new GetClientPayloadQuery.Parameter("Cl")))
            .UnwrapBox();
        Assert.Single(result.Items);
        Assert.Equal("Client1", result.Items.First().Client.ClientName);
    }
    [Fact]
    public async Task NextQueryTest() =>
        await ResultBox.WrapTry(RemoveAllFromDefaultAndDissolvableWithResultBox)
            .Conveyor(_ => commandExecutor.ExecCommandNextAsync(new CreateBranch { Name = "Branch 24" }))
            .Conveyor(response => response.GetAggregateId().Remap(BranchId.FromValue))
            .Conveyor(branchId => commandExecutor.ExecCommandNextAsync(new CreateClient(branchId.Value, "Client1", "test@example.com")))
            .Conveyor(_ => queryExecutor.ExecuteNextAsync(new ClientEmailExistQueryNext("test@example.com")))
            .Scan(Assert.True)
            .Combine(_ => queryExecutor.ExecuteNextAsync(new ClientEmailExistQueryNextAsync("test@example.com")))
            .Scan(Assert.Equal)
            .Conveyor(_ => queryExecutor.ExecuteNextAsync(new ClientEmailExistQueryNext("test@examplesssss.com")))
            .Scan(Assert.False)
            .Combine(_ => queryExecutor.ExecuteNextAsync(new ClientEmailExistQueryNextAsync("test@examplesssss.com")))
            .Scan(Assert.Equal)
            .ScanResult(result => Assert.True(result.IsSuccess));
    [Fact]
    public Task NextQueryTest2() =>
        ResultBox.WrapTry(RemoveAllFromDefaultAndDissolvableWithResultBox)
            .Conveyor(_ => commandExecutor.ExecCommandNextAsync(new CreateBranch { Name = "Branch 24" }))
            .Conveyor(response => response.GetAggregateId().Remap(BranchId.FromValue))
            .Conveyor(branchId => commandExecutor.ExecCommandNextAsync(new CreateClient(branchId.Value, "Client1", "test@example.com")))
            .Conveyor(_ => queryExecutor.ExecuteNextAsync(new GetClientPayloadQueryNext("Client1")))
            .Combine(_ => ResultBox.FromValue(queryExecutor.ExecuteAsync(new GetClientPayloadQuery.Parameter("Client1"))))
            .Scan(Assert.Equal)
            .Conveyor(_ => queryExecutor.ExecuteNextAsync(new GetClientPayloadQueryNextAsync("Client1")))
            .Combine(_ => ResultBox.FromValue(queryExecutor.ExecuteAsync(new GetClientPayloadQuery.Parameter("Client1"))))
            .Scan(Assert.Equal)
            .Conveyor(_ => queryExecutor.ExecuteNextAsync(new GetClientPayloadQueryNext("Cli---ent1")))
            .Combine(_ => ResultBox.FromValue(queryExecutor.ExecuteAsync(new GetClientPayloadQuery.Parameter("Cli---ent1"))))
            .Scan(Assert.Equal)
            .Scan(Assert.Equal)
            .Conveyor(_ => queryExecutor.ExecuteNextAsync(new GetClientPayloadQueryNextAsync("Cli---ent1")))
            .Combine(_ => ResultBox.FromValue(queryExecutor.ExecuteAsync(new GetClientPayloadQuery.Parameter("Cli---ent1"))))
            .Scan(Assert.Equal);


    [Fact]
    public Task NextQueryTestWithProjection() =>
        ResultBox.WrapTry(RemoveAllFromDefaultAndDissolvableWithResultBox)
            .Conveyor(_ => commandExecutor.ExecCommandNextAsync(new CreateBranch(_branchName)))
            .Conveyor(response => response.GetAggregateId().Remap(BranchId.FromValue))
            .Do(branchId => commandExecutor.ExecCommandNextAsync(new CreateClient(branchId.Value, _clientNameBase + 1, "test" + 1 + "@example.com")))
            .Do(branchId => commandExecutor.ExecCommandNextAsync(new CreateClient(branchId.Value, _clientNameBase + 2, "test" + 2 + "@example.com")))
            .Do(branchId => commandExecutor.ExecCommandNextAsync(new CreateClient(branchId.Value, _clientNameBase + 3, "test" + 3 + "@example.com")))
            .Do(branchId => commandExecutor.ExecCommandNextAsync(new CreateClient(branchId.Value, _clientNameBase + 4, "test" + 4 + "@example.com")))
            .Do(branchId => commandExecutor.ExecCommandNextAsync(new CreateClient(branchId.Value, _clientNameBase + 5, "test" + 5 + "@example.com")))
            .Conveyor(
                _ => queryExecutor.ExecuteNextAsync(
                    new ClientLoyaltyPointQueryNext(
                        null,
                        null,
                        3,
                        1,
                        null,
                        null,
                        null,
                        null)))
            .Combine(
                _ => ResultBox.WrapTry(
                    () => queryExecutor.ExecuteAsync(
                        new ClientLoyaltyPointQuery.Parameter(
                            null,
                            null,
                            3,
                            1,
                            null,
                            null,
                            null,
                            null))))
            .Scan(Assert.Equal);
}
