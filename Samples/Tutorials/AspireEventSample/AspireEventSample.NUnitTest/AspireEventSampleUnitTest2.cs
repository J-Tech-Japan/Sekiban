using AspireEventSample.ApiService.Aggregates.Branches;
using AspireEventSample.ApiService.Generated;
using AspireEventSample.ApiService.Projections;
using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.NUnit;
namespace AspireEventSample.NUnitTest;

public class AspireEventSampleUnitTest2 : SekibanInMemoryTestBase
{
    protected override SekibanDomainTypes GetDomainTypes() => AspireEventSampleApiServiceDomainTypes.Generate(
        AspireEventSampleApiServiceEventsJsonContext.Default.Options);

    [Test]
    public void RegisterBranchTest()
        => GivenCommandWithResult(new RegisterBranch("DDD", "Japan"))
            .Do(response => Assert.That(response.Version, Is.EqualTo(1)))
            .Conveyor(response => WhenCommandWithResult(new ChangeBranchName(response.PartitionKeys.AggregateId, "ES")))
            .Do(response => Assert.That(response.Version, Is.EqualTo(2)))
            .Conveyor(response => ThenGetAggregateWithResult<BranchProjector>(response.PartitionKeys))
            .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<Branch>())
            .Do(payload => Assert.That(payload.Name, Is.EqualTo("ES")))
            .UnwrapBox();

    [Test]
    public void RegisterBranchAnd___QueryTest2()
        => GivenCommandWithResult(new RegisterBranch("DDD", "Japan"))
            .Conveyor(
                response => GivenCommandWithResult(new ChangeBranchName(response.PartitionKeys.AggregateId, "ES")))
            .Conveyor(_ => ThenQueryWithResult(new BranchExistsQuery("DDD")))
            .Do(queryResult => Assert.That(queryResult, Is.False))
            .Conveyor(_ => ThenQueryWithResult(new BranchExistsQuery("ES")))
            .Do(queryResult => Assert.That(queryResult, Is.True))
            .UnwrapBox();
    [Test]
    public void RegisterBranchAndListQuery()
        => GivenCommandWithResult(new RegisterBranch("DDD", "Japan"))
            .Conveyor(
                response => GivenCommandWithResult(new ChangeBranchName(response.PartitionKeys.AggregateId, "ES")))
            .Conveyor(_ => ThenQueryWithResult(new SimpleBranchListQuery("DDD")))
            .Do(queryResult => Assert.That(queryResult.Items.Count(), Is.EqualTo(0)))
            .Conveyor(_ => ThenQueryWithResult(new SimpleBranchListQuery("ES")))
            .Do(queryResult => Assert.That(queryResult.Items.Count(), Is.EqualTo(1)))
            .UnwrapBox();

    [Test]
    public void RegisterTwoBranchTest()
        => GivenCommandWithResult(new RegisterBranch("DDD", "Japan"))
            .Do(_ => Assert.That(Repository.Events, Has.Count.EqualTo(1)))
            .Conveyor(_ => GivenCommandWithResult(new RegisterBranch("DDD2", "USA")))
            .Do(_ => Assert.That(Repository.Events, Has.Count.EqualTo(2)))
            .Do(response => Assert.That(response.Version, Is.EqualTo(1)))
            .Conveyor(response => WhenCommandWithResult(new ChangeBranchName(response.PartitionKeys.AggregateId, "ES")))
            .Do(response => Assert.That(response.Version, Is.EqualTo(2)))
            .Conveyor(response => ThenGetAggregateWithResult<BranchProjector>(response.PartitionKeys))
            .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<Branch>())
            .Do(payload => Assert.That(payload.Name, Is.EqualTo("ES")))
            .UnwrapBox();
}
