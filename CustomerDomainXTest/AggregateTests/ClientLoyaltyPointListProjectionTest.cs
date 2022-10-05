using CustomerDomainContext.Projections.ClientLoyaltyPointLists;
using Sekiban.EventSourcing.TestHelpers;
using Xunit;
namespace CustomerDomainXTest.AggregateTests;

public class ClientLoyaltyPointListProjectionTest : CustomerMultipleAggregateProjectionTestBase<ClientLoyaltyPointListProjection,
    ClientLoyaltyPointListProjection.ContentsDefinition>
{
    public ProjectionListQueryFilterTestChecker<ClientLoyaltyPointListProjection, ClientLoyaltyPointListProjection.ContentsDefinition,
        ClientLoyaltyPointQueryFilter, ClientLoyaltyPointQueryFilter.QueryFilterParameter,
        ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord> ListQueryFilterTestChecker = new();

    [Fact]
    public void RegularProjection()
    {

        GivenQueryFilterChecker(ListQueryFilterTestChecker)
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

        ListQueryFilterTestChecker.WhenParam(
                new ClientLoyaltyPointQueryFilter.QueryFilterParameter(
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null))
            .WriteResponse("ClientLoyaltyPointQueryFilter.json");
    }
}
