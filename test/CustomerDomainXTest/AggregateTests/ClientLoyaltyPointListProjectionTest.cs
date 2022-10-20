using CustomerDomainContext.Aggregates.Branches.Commands;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Projections.ClientLoyaltyPointLists;
using CustomerDomainContext.Shared;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Testing.QueryFilter;
using System;
using System.Collections.Generic;
using Xunit;
namespace CustomerDomainXTest.AggregateTests;

public class ClientLoyaltyPointListProjectionTest : CustomerMultipleAggregateProjectionTestBase<ClientLoyaltyPointListProjection,
    ClientLoyaltyPointListProjection.ContentsDefinition, CustomerDependency>
{

    public Guid _branchId = Guid.NewGuid();
    public string _branchName = "TESTBRANCH";

    public Guid _clientId1 = Guid.NewGuid();
    public Guid _clientId2 = Guid.NewGuid();
    public Guid _clientId3 = Guid.NewGuid();
    public Guid _clientId4 = Guid.NewGuid();
    public Guid _clientId5 = Guid.NewGuid();
    public string _clientNameBase = "Client TEST ";
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
            .WriteProjectionToFile("TestData1ResultOut.json");
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
            .WriteResponseToFile("ClientLoyaltyPointQueryFilter.json");
    }


    [Fact]
    public void CommandTest1()
    {
        GivenQueryFilterChecker(ListQueryFilterTestChecker);
        RunCreateCommand(new CreateBranch(_branchName), _branchId);
        RunCreateCommand(new CreateClient(_branchId, _clientNameBase + 1, "test" + 1 + "@example.com"), _clientId1);
        RunCreateCommand(new CreateClient(_branchId, _clientNameBase + 2, "test" + 2 + "@example.com"), _clientId2);
        RunCreateCommand(new CreateClient(_branchId, _clientNameBase + 3, "test" + 3 + "@example.com"), _clientId3);
        RunCreateCommand(new CreateClient(_branchId, _clientNameBase + 4, "test" + 4 + "@example.com"), _clientId4);
        RunCreateCommand(new CreateClient(_branchId, _clientNameBase + 5, "test" + 5 + "@example.com"), _clientId5);

        WhenProjection().ThenNotThrowsAnException();
    }
    [Fact]
    public void QueryFilterBasic1()
    {
        GivenScenario(CommandTest1);
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
            .ThenResponseIs(
                new QueryFilterListResult<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>(
                    5,
                    null,
                    null,
                    null,
                    new List<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>
                    {
                        new(_branchId, _branchName, _clientId1, _clientNameBase + "1", 0),
                        new(_branchId, _branchName, _clientId2, _clientNameBase + "2", 0),
                        new(_branchId, _branchName, _clientId3, _clientNameBase + "3", 0),
                        new(_branchId, _branchName, _clientId4, _clientNameBase + "4", 0),
                        new(_branchId, _branchName, _clientId5, _clientNameBase + "5", 0)
                    }));
    }
    [Fact]
    public void QueryFilterBasicPaging()
    {
        GivenScenario(CommandTest1);
        ListQueryFilterTestChecker.WhenParam(
                new ClientLoyaltyPointQueryFilter.QueryFilterParameter(
                    null,
                    null,
                    3,
                    1,
                    null,
                    null,
                    null,
                    null))
            .ThenResponseIs(
                new QueryFilterListResult<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>(
                    5,
                    2,
                    1,
                    3,
                    new List<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>
                    {
                        new(_branchId, _branchName, _clientId1, _clientNameBase + "1", 0),
                        new(_branchId, _branchName, _clientId2, _clientNameBase + "2", 0),
                        new(_branchId, _branchName, _clientId3, _clientNameBase + "3", 0)
                    }));
    }
    [Fact]
    public void QueryFilterBasicPaging2()
    {
        GivenScenario(CommandTest1);
        ListQueryFilterTestChecker.WhenParam(
                new ClientLoyaltyPointQueryFilter.QueryFilterParameter(
                    null,
                    null,
                    5,
                    1,
                    null,
                    null,
                    null,
                    null))
            .ThenResponseIs(
                new QueryFilterListResult<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>(
                    5,
                    1,
                    1,
                    5,
                    new List<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>
                    {
                        new(_branchId, _branchName, _clientId1, _clientNameBase + "1", 0),
                        new(_branchId, _branchName, _clientId2, _clientNameBase + "2", 0),
                        new(_branchId, _branchName, _clientId3, _clientNameBase + "3", 0),
                        new(_branchId, _branchName, _clientId4, _clientNameBase + "4", 0),
                        new(_branchId, _branchName, _clientId5, _clientNameBase + "5", 0)
                    }));
    }
    [Fact]
    public void QueryFilterBasicPaging3()
    {
        GivenScenario(CommandTest1);
        ListQueryFilterTestChecker.WhenParam(
                new ClientLoyaltyPointQueryFilter.QueryFilterParameter(
                    null,
                    null,
                    3,
                    2,
                    null,
                    null,
                    null,
                    null))
            .ThenResponseIs(
                new QueryFilterListResult<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>(
                    5,
                    2,
                    2,
                    3,
                    new List<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>
                    {
                        new(_branchId, _branchName, _clientId4, _clientNameBase + "4", 0),
                        new(_branchId, _branchName, _clientId5, _clientNameBase + "5", 0)
                    }));
    }
    [Fact]
    public void QueryFilterBasicPagingRequestOverflowed()
    {
        GivenScenario(CommandTest1);
        ListQueryFilterTestChecker.WhenParam(
                new ClientLoyaltyPointQueryFilter.QueryFilterParameter(
                    null,
                    null,
                    3,
                    3,
                    null,
                    null,
                    null,
                    null))
            .ThenResponseIs(
                new QueryFilterListResult<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>(
                    5,
                    2,
                    3,
                    3,
                    new List<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>()));
    }
    [Fact]
    public void QueryFilterBasicPagingRequestZero()
    {
        GivenScenario(CommandTest1);
        ListQueryFilterTestChecker.WhenParam(
                new ClientLoyaltyPointQueryFilter.QueryFilterParameter(
                    null,
                    null,
                    3,
                    0,
                    null,
                    null,
                    null,
                    null))
            .ThenResponseIs(
                new QueryFilterListResult<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>(
                    5,
                    2,
                    0,
                    3,
                    new List<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>()));
    }
}
