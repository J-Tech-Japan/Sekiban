using CustomerDomainContext.Projections.ClientLoyaltyPointLists;
using CustomerDomainContext.Shared;
using Sekiban.EventSourcing.TestHelpers;
using Xunit;
namespace CustomerDomainXTest.AggregateTests;

public class ClientLoyaltyPointListProjectionTest : CommonMultipleAggregateProjectionTestBase<ClientLoyaltyPointListProjection,
    ClientLoyaltyPointListProjection.ContentsDefinition>
{
    public ProjectionQueryListFilterTestChecker<ClientLoyaltyPointListProjection, ClientLoyaltyPointListProjection.ContentsDefinition,
        ClientLoyaltyPointQueryFilterFilter, ClientLoyaltyPointQueryFilterFilter.QueryFilterParameter,
        ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord> _queryListFilterTestChecker = new();
    public ClientLoyaltyPointListProjectionTest() : base(CustomerDependency.GetOptions()) { }

    [Fact]
    public void RegularProjection()
    {

        GivenQueryFilterChecker(_queryListFilterTestChecker)
            .GivenEventsFromFile("TestData1.json")
            .WhenProjection()
            .ThenNotThrowsAnException()
//        await ThenDtoFileAsync("TestData1Result.json");
            .WriteProjectionToFileAsync("TestData1ResultOut.json");
    }
    [Fact]
    public void QueryFilterCheckerTest()
    {
        GivenScenario(RegularProjection);

        _queryListFilterTestChecker.WhenParam(
                new ClientLoyaltyPointQueryFilterFilter.QueryFilterParameter(
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null))
            .WriteResponse("ClientLoyaltyPointQueryFilterFilter.json");
    }
}
