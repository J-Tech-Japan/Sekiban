using CustomerDomainContext.Projections.ClientLoyaltyPointLists;
using Sekiban.EventSourcing.TestHelpers;
using Xunit;
namespace CustomerDomainXTest.AggregateTests;

public class ClientLoyaltyPointListProjectionTest : MultipleAggregateProjectionTestBase<ClientLoyaltyPointListProjection,
    ClientLoyaltyPointListProjection.ContentsDefinition>
{
    public ProjectionQueryListFilterTestChecker<ClientLoyaltyPointListProjection, ClientLoyaltyPointListProjection.ContentsDefinition,
        ClientLoyaltyPointQueryFilter, ClientLoyaltyPointQueryFilter.QueryFilterParameter,
        ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord> _queryListFilterTestChecker;
    public ClientLoyaltyPointListProjectionTest()
    {
        _queryListFilterTestChecker
            = GetService<ProjectionQueryListFilterTestChecker<ClientLoyaltyPointListProjection, ClientLoyaltyPointListProjection.ContentsDefinition,
                ClientLoyaltyPointQueryFilter, ClientLoyaltyPointQueryFilter.QueryFilterParameter,
                ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>>();
    }

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
