using AspireEventSample.ApiService.Aggregates.Branches;
using AspireEventSample.ApiService.Generated;
using AspireEventSample.ApiService.Projections;
using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.xUnit;
namespace AspireEventSample.UnitTest;

public class AspireEventSampleUnitTest2 : SekibanInMemoryTestBase
{
    protected override SekibanDomainTypes GetDomainTypes() => AspireEventSampleApiServiceDomainTypes.Generate(
        AspireEventSampleApiServiceEventsJsonContext.Default.Options);

    [Fact]
    public void RegisterBranchTest()
        => GivenCommandWithResult(new RegisterBranch("DDD", "Japan"))
            .Do(response => Assert.Equal(1, response.Version))
            .Conveyor(response => WhenCommandWithResult(new ChangeBranchName(response.PartitionKeys.AggregateId, "ES")))
            .Do(response => Assert.Equal(2, response.Version))
            .Conveyor(response => ThenGetAggregateWithResult<BranchProjector>(response.PartitionKeys))
            .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<Branch>())
            .Do(payload => Assert.Equal("ES", payload.Name))
            .UnwrapBox();

    [Fact]
    public void RegisterTwoBranchTest()
        => GivenCommandWithResult(new RegisterBranch("DDD", "Japan"))
            .Do(_ => Assert.Single(Repository.Events))
            .Conveyor(_ => GivenCommandWithResult(new RegisterBranch("DDD2", "USA")))
            .Do(_ => Assert.Equal(2, Repository.Events.Count))
            .Do(response => Assert.Equal(1, response.Version))
            .Conveyor(response => WhenCommandWithResult(new ChangeBranchName(response.PartitionKeys.AggregateId, "ES")))
            .Do(response => Assert.Equal(2, response.Version))
            .Conveyor(response => ThenGetAggregateWithResult<BranchProjector>(response.PartitionKeys))
            .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<Branch>())
            .Do(payload => Assert.Equal("ES", payload.Name))
            .UnwrapBox();

    [Fact]
    public void RegisterBranchAndQueryTest()
        => GivenCommandWithResult(new RegisterBranch("DDD", "Japan"))
            .Conveyor(
                response => GivenCommandWithResult(new ChangeBranchName(response.PartitionKeys.AggregateId, "ES")))
            .Conveyor(_ => ThenQueryWithResult(new BranchExistsQuery("DDD")))
            .Do(Assert.False)
            .Conveyor(_ => ThenQueryWithResult(new BranchExistsQuery("ES")))
            .Do(Assert.True)
            .UnwrapBox();

    [Fact]
    public void RegisterBranchAndListQuery()
        => GivenCommandWithResult(new RegisterBranch("DDD", "Japan"))
            .Conveyor(
                response => GivenCommandWithResult(new ChangeBranchName(response.PartitionKeys.AggregateId, "ES")))
            .Conveyor(_ => ThenQueryWithResult(new SimpleBranchListQuery("DDD")))
            .Do(queryResult => Assert.Empty(queryResult.Items))
            .Conveyor(_ => ThenQueryWithResult(new SimpleBranchListQuery("ES")))
            .Do(queryResult => Assert.Single(queryResult.Items))
            .UnwrapBox();
}
