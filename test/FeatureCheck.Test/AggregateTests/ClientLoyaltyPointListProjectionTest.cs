using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using FeatureCheck.Domain.Shared;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Testing;
using System;
using System.Collections.Generic;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class ClientLoyaltyPointListProjectionTest : UnifiedTest<CustomerDependency>
{
    public Guid _branchId = Guid.NewGuid();
    public string _branchName = "TESTBRANCH";

    public Guid _clientId1 = Guid.NewGuid();
    public Guid _clientId2 = Guid.NewGuid();
    public Guid _clientId3 = Guid.NewGuid();
    public Guid _clientId4 = Guid.NewGuid();
    public Guid _clientId5 = Guid.NewGuid();
    public string _clientNameBase = "CreateClient TEST ";

    [Fact]
    public void RegularProjection()
    {
        GivenEventsFromFile("TestData1.json")
            .WriteMultiProjectionStateToFile<ClientLoyaltyPointListProjection>("TestData1ResultOut.json");
    }

    [Fact]
    public void QueryTest()
    {
        GivenScenario(RegularProjection);
        WriteMultiProjectionListQueryResponseToFile<ClientLoyaltyPointListProjection, ClientLoyaltyPointQuery,
            ClientLoyaltyPointQuery.QueryParameter,
            ClientLoyaltyPointQuery.Response>(
            new ClientLoyaltyPointQuery.QueryParameter(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            "ClientLoyaltyPointQuery.json"
        );
    }


    [Fact]
    public void CommandTest1()
    {
        RunCommand(new CreateBranch(_branchName), _branchId);
        RunCommand(new CreateClient(_branchId, _clientNameBase + 1, "test" + 1 + "@example.com"), _clientId1);
        RunCommand(new CreateClient(_branchId, _clientNameBase + 2, "test" + 2 + "@example.com"), _clientId2);
        RunCommand(new CreateClient(_branchId, _clientNameBase + 3, "test" + 3 + "@example.com"), _clientId3);
        RunCommand(new CreateClient(_branchId, _clientNameBase + 4, "test" + 4 + "@example.com"), _clientId4);
        RunCommand(new CreateClient(_branchId, _clientNameBase + 5, "test" + 5 + "@example.com"), _clientId5);
        ThenGetMultiProjectionPayload<ClientLoyaltyPointListProjection>(
            projection =>
                Assert.NotEmpty(projection.Branches));
    }

    [Fact]
    public void QueryBasic1()
    {
        GivenScenario(CommandTest1);
        ThenMultiProjectionListQueryResponseIs<ClientLoyaltyPointListProjection, ClientLoyaltyPointQuery,
            ClientLoyaltyPointQuery.QueryParameter,
            ClientLoyaltyPointQuery.Response>(
            new ClientLoyaltyPointQuery.QueryParameter(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            new ListQueryResult<ClientLoyaltyPointQuery.Response>(
                5,
                null,
                null,
                null,
                new List<ClientLoyaltyPointQuery.Response>
                {
                    new(_branchId, _branchName, _clientId1, _clientNameBase + "1", 0),
                    new(_branchId, _branchName, _clientId2, _clientNameBase + "2", 0),
                    new(_branchId, _branchName, _clientId3, _clientNameBase + "3", 0),
                    new(_branchId, _branchName, _clientId4, _clientNameBase + "4", 0),
                    new(_branchId, _branchName, _clientId5, _clientNameBase + "5", 0)
                }));
    }

    [Fact]
    public void QueryBasicPaging()
    {
        GivenScenario(CommandTest1);
        ThenMultiProjectionListQueryResponseIs<ClientLoyaltyPointListProjection, ClientLoyaltyPointQuery,
            ClientLoyaltyPointQuery.QueryParameter,
            ClientLoyaltyPointQuery.Response>(
            new ClientLoyaltyPointQuery.QueryParameter(
                null,
                null,
                3,
                1,
                null,
                null,
                null,
                null),
            new ListQueryResult<ClientLoyaltyPointQuery.Response>(
                5,
                2,
                1,
                3,
                new List<ClientLoyaltyPointQuery.Response>
                {
                    new(_branchId, _branchName, _clientId1, _clientNameBase + "1", 0),
                    new(_branchId, _branchName, _clientId2, _clientNameBase + "2", 0),
                    new(_branchId, _branchName, _clientId3, _clientNameBase + "3", 0)
                }));
    }

    [Fact]
    public void QueryBasicPaging2()
    {
        GivenScenario(CommandTest1);
        ThenMultiProjectionListQueryResponseIs<ClientLoyaltyPointListProjection, ClientLoyaltyPointQuery,
            ClientLoyaltyPointQuery.QueryParameter,
            ClientLoyaltyPointQuery.Response>(
            new ClientLoyaltyPointQuery.QueryParameter(
                null,
                null,
                5,
                1,
                null,
                null,
                null,
                null),
            new ListQueryResult<ClientLoyaltyPointQuery.Response>(
                5,
                1,
                1,
                5,
                new List<ClientLoyaltyPointQuery.Response>
                {
                    new(_branchId, _branchName, _clientId1, _clientNameBase + "1", 0),
                    new(_branchId, _branchName, _clientId2, _clientNameBase + "2", 0),
                    new(_branchId, _branchName, _clientId3, _clientNameBase + "3", 0),
                    new(_branchId, _branchName, _clientId4, _clientNameBase + "4", 0),
                    new(_branchId, _branchName, _clientId5, _clientNameBase + "5", 0)
                }));
    }

    [Fact]
    public void QueryBasicPaging3()
    {
        GivenScenario(CommandTest1);
        ThenMultiProjectionListQueryResponseIs<ClientLoyaltyPointListProjection, ClientLoyaltyPointQuery,
            ClientLoyaltyPointQuery.QueryParameter,
            ClientLoyaltyPointQuery.Response>(
            new ClientLoyaltyPointQuery.QueryParameter(
                null,
                null,
                3,
                2,
                null,
                null,
                null,
                null),
            new ListQueryResult<ClientLoyaltyPointQuery.Response>(
                5,
                2,
                2,
                3,
                new List<ClientLoyaltyPointQuery.Response>
                {
                    new(_branchId, _branchName, _clientId4, _clientNameBase + "4", 0),
                    new(_branchId, _branchName, _clientId5, _clientNameBase + "5", 0)
                }));
    }

    [Fact]
    public void QueryBasicPagingRequestOverflowed()
    {
        GivenScenario(CommandTest1);
        ThenMultiProjectionListQueryResponseIs<ClientLoyaltyPointListProjection, ClientLoyaltyPointQuery,
            ClientLoyaltyPointQuery.QueryParameter,
            ClientLoyaltyPointQuery.Response>(
            new ClientLoyaltyPointQuery.QueryParameter(
                null,
                null,
                3,
                3,
                null,
                null,
                null,
                null),
            new ListQueryResult<ClientLoyaltyPointQuery.Response>(
                5,
                2,
                3,
                3,
                new List<ClientLoyaltyPointQuery.Response>()));
    }

    [Fact]
    public void QueryBasicPagingRequestZero()
    {
        GivenScenario(CommandTest1);
        ThenMultiProjectionListQueryResponseIs<ClientLoyaltyPointListProjection, ClientLoyaltyPointQuery,
            ClientLoyaltyPointQuery.QueryParameter,
            ClientLoyaltyPointQuery.Response>(
            new ClientLoyaltyPointQuery.QueryParameter(
                null,
                null,
                3,
                0,
                null,
                null,
                null,
                null),
            new ListQueryResult<ClientLoyaltyPointQuery.Response>(
                5,
                2,
                0,
                3,
                new List<ClientLoyaltyPointQuery.Response>()));
    }
}
