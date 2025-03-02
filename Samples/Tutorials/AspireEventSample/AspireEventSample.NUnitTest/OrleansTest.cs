using AspireEventSample.ApiService.Aggregates.Branches;
using AspireEventSample.ApiService.Aggregates.Carts;
using AspireEventSample.ApiService.Generated;
using AspireEventSample.ApiService.Projections;
using Orleans.Serialization;
using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Orleans.NUnit;
using Sekiban.Pure.Projectors;
namespace AspireEventSample.NUnitTest;

public class OrleansTest : SekibanOrleansTestBase<OrleansTest>
{
    [Test]
    public void Test1()
        => GivenCommandWithResult(new RegisterBranch("DDD", "Japan"))
            .Do(response => Assert.That(response.Version, Is.EqualTo(1)))
            .Conveyor(response => WhenCommandWithResult(new ChangeBranchName(response.PartitionKeys.AggregateId, "ES")))
            .Do(response => Assert.That(response.Version, Is.EqualTo(2)))
            .Conveyor(response => ThenGetAggregateWithResult<BranchProjector>(response.PartitionKeys))
            .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<Branch>())
            .Do(payload =>
            {
                Assert.That(payload.Name, Is.EqualTo("ES"));
                Assert.That(payload.Country, Is.EqualTo("Japan"));
            })
            .Conveyor(_ => ThenGetMultiProjectorWithResult<BranchMultiProjector>())
            .Do(
                projector =>
                {
                    Assert.That(projector.Branches.Count, Is.EqualTo(1));
                    Assert.That(projector.Branches.Values.First().BranchName, Is.EqualTo("ES"));
                })
            .Conveyor(_ => ThenGetMultiProjectorWithResult<AggregateListProjector<BranchProjector>>())
            .Do(
                projector =>
                {
                    Assert.That(projector.Aggregates.Values.Count, Is.EqualTo(1));
                    Assert.That(projector.Aggregates.Values.First().Payload, Is.TypeOf<Branch>());
                    var branch = (Branch)projector.Aggregates.Values.First().Payload;
                    Assert.That(branch.Name, Is.EqualTo("ES"));
                    Assert.That(branch.Country, Is.EqualTo("Japan"));
                })
            .UnwrapBox();

    [Test]
    public void TestCreateShoppingCartThrows()
    {
        Assert.Throws<AggregateException>(
            () =>
            {
                WhenCommandWithResult(new CreateShoppingCart(Guid.CreateVersion7())).UnwrapBox();
            });
    }

    public override SekibanDomainTypes GetDomainTypes() =>
        AspireEventSampleApiServiceDomainTypes.Generate(AspireEventSampleApiServiceEventsJsonContext.Default.Options);

    [Test]
    public void RegisterBranchAndListQueryTest()
        => GivenCommandWithResult(new RegisterBranch("DDD", "Japan"))
            .Conveyor(
                response => GivenCommandWithResult(new ChangeBranchName(response.PartitionKeys.AggregateId, "ES")))
            .Conveyor(_ => ThenQueryWithResult(new BranchExistsQuery("ES")))
            .Do(queryResult => Assert.That(queryResult, Is.True))
            .Conveyor(_ => ThenQueryWithResult(new SimpleBranchListQuery("DDD")))
            .Do(queryResult => Assert.That(queryResult.Items, Is.Empty))
            .Conveyor(_ => ThenQueryWithResult(new SimpleBranchListQuery("ES")))
            .Do(queryResult => Assert.That(queryResult.Items.Count(), Is.EqualTo(1)))
            .UnwrapBox();
}
