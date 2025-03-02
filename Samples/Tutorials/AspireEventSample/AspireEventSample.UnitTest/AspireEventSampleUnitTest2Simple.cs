using AspireEventSample.ApiService.Aggregates.Branches;
using AspireEventSample.ApiService.Generated;
using AspireEventSample.ApiService.Projections;
using Sekiban.Pure;
using Sekiban.Pure.xUnit;
namespace AspireEventSample.UnitTest;

public class AspireEventSampleUnitTest2Simple : SekibanInMemoryTestBase
{
    protected override SekibanDomainTypes GetDomainTypes() => AspireEventSampleApiServiceDomainTypes.Generate(
        AspireEventSampleApiServiceEventsJsonContext.Default.Options);

    [Fact]
    public void RegisterBranchTest()
    {
        var response1 = GivenCommand(new RegisterBranch("DDD", "Japan"));
        Assert.Equal(1, response1.Version);

        var response2 = WhenCommand(new ChangeBranchName(response1.PartitionKeys.AggregateId, "ES"));
        Assert.Equal(2, response2.Version);

        var aggregate = ThenGetAggregate<BranchProjector>(response2.PartitionKeys);
        var branch = (Branch)aggregate.Payload;
        Assert.Equal("ES", branch.Name);
    }

    [Fact]
    public void RegisterTwoBranchTest()
    {
        var response1 = GivenCommand(new RegisterBranch("DDD", "Japan"));
        Assert.Single(Repository.Events);

        var response2 = GivenCommand(new RegisterBranch("DDD2", "USA"));
        Assert.Equal(2, Repository.Events.Count);
        Assert.Equal(1, response2.Version);

        var response3 = WhenCommand(new ChangeBranchName(response2.PartitionKeys.AggregateId, "ES"));
        Assert.Equal(2, response3.Version);

        var aggregate = ThenGetAggregate<BranchProjector>(response3.PartitionKeys);
        var branch = (Branch)aggregate.Payload;
        Assert.Equal("ES", branch.Name);
    }

    [Fact]
    public void RegisterBranchAndQueryTest()
    {
        var response1 = GivenCommand(new RegisterBranch("DDD", "Japan"));
        var response2 = GivenCommand(new ChangeBranchName(response1.PartitionKeys.AggregateId, "ES"));

        var queryResult1 = ThenQuery(new BranchExistsQuery("DDD"));
        Assert.False(queryResult1);

        var queryResult2 = ThenQuery(new BranchExistsQuery("ES"));
        Assert.True(queryResult2);
    }

    [Fact]
    public void RegisterBranchAndListQuery()
    {
        var response1 = GivenCommand(new RegisterBranch("DDD", "Japan"));
        var response2 = GivenCommand(new ChangeBranchName(response1.PartitionKeys.AggregateId, "ES"));

        var queryResult1 = ThenQuery(new SimpleBranchListQuery("DDD"));
        Assert.Empty(queryResult1.Items);

        var queryResult2 = ThenQuery(new SimpleBranchListQuery("ES"));
        Assert.Single(queryResult2.Items);
    }
}
