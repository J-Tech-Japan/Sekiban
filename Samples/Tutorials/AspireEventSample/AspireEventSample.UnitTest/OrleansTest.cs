using AspireEventSample.ApiService.Aggregates.Branches;
using AspireEventSample.ApiService.Aggregates.Carts;
using AspireEventSample.ApiService.Generated;
using AspireEventSample.ApiService.Projections;
using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Orleans.xUnit;
using Sekiban.Pure.Projectors;
namespace AspireEventSample.UnitTest;

public class OrleansTest : SekibanOrleansTestBase<OrleansTest>
{
    [Fact]
    public void Test1() =>
        GivenCommandWithResult(new RegisterBranch("DDD", "Japan"))
            .Do(response => Assert.Equal(1, response.Version))
            .Conveyor(response => WhenCommandWithResult(new ChangeBranchName(response.PartitionKeys.AggregateId, "ES")))
            .Do(response => Assert.Equal(2, response.Version))
            // .Do(_ => Task.Delay(10000))
            .Conveyor(response => ThenGetAggregateWithResult<BranchProjector>(response.PartitionKeys))
            .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<Branch>())
            .Do(payload =>
            {
                Assert.Equal("ES", payload.Name);
                Assert.Equal("Japan", payload.Country);
            })
            .Conveyor(_ => ThenGetMultiProjectorWithResult<BranchMultiProjector>())
            .Do(
                projector =>
                {
                    Assert.Equal(1, projector.Branches.Count);
                    Assert.Equal("ES", projector.Branches.Values.First().BranchName);
                })
            .Conveyor(_ => ThenGetMultiProjectorWithResult<AggregateListProjector<BranchProjector>>())
            .Do(
                projector =>
                {
                    Assert.Equal(1, projector.Aggregates.Values.Count());
                    Assert.IsType<Branch>(projector.Aggregates.Values.First().Payload);
                    var branch = (Branch)projector.Aggregates.Values.First().Payload;
                    Assert.Equal("ES", branch.Name);
                    Assert.Equal("Japan", branch.Country);
                })
            .UnwrapBox();

    [Fact]
    public void TestCreateShoppingCartThrows()
    {
        Assert.Throws<AggregateException>(
            () =>
            {
                WhenCommandWithResult(new CreateShoppingCart(Guid.CreateVersion7())).UnwrapBox();
            });
    }
    [Fact]
    public void TestSerializable()
    {
        CheckSerializability(new CreateShoppingCart(Guid.CreateVersion7()));
    }

    public override SekibanDomainTypes GetDomainTypes() =>
        AspireEventSampleApiServiceDomainTypes.Generate(AspireEventSampleApiServiceEventsJsonContext.Default.Options);

    [Fact]
    public void RegisterBranchAndListQueryTest() =>
        GivenCommandWithResult(new RegisterBranch("DDD", "Japan"))
            .Conveyor(
                response => GivenCommandWithResult(new ChangeBranchName(response.PartitionKeys.AggregateId, "ES")))
            // .Do(_ => Task.Delay(10000))
            .Conveyor(_ => ThenQueryWithResult(new BranchExistsQuery("ES")))
            .Do(queryResult => Assert.True(queryResult))
            .Conveyor(_ => ThenQueryWithResult(new SimpleBranchListQuery("DDD")))
            .Do(queryResult => Assert.Empty(queryResult.Items))
            .Conveyor(_ => ThenQueryWithResult(new SimpleBranchListQuery("ES")))
            .Do(queryResult => Assert.Equal(1, queryResult.Items.Count()))
            .UnwrapBox();
}
