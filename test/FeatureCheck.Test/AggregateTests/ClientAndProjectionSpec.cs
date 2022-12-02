using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.Clients.Projections;
using Customer.Domain.Aggregates.Clients.Queries;
using Customer.Domain.Aggregates.Clients.Queries.BasicClientFilters;
using Customer.Domain.Shared;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Testing.SingleProjections;
using Xunit;

namespace Customer.Test.AggregateTests;

public class ClientAndProjectionSpec : AggregateTest<Client, CustomerDependency>
{
    public readonly string branchName = "BranchName";
    public readonly string clientEmail = "client@example.com";
    public readonly string clientName = "Test Client";
    public readonly string clientNameChanged = "Test Client Changed";
    public Guid branchId = Guid.Parse("cdb93f86-8d2f-442c-9f62-b9e791401f5f");
    public DateTime FirstEventDatetime { get; set; } = DateTime.Now;
    public DateTime ChangedEventDatetime { get; set; } = DateTime.Now;


    [Fact]
    public void CreateTest()
    {
        RunEnvironmentCommand(new CreateBranch(branchName), branchId);
        // GetEnvironmentAggregateStateのテスト
        var branch = GetEnvironmentAggregateState<Branch>(branchId);
        Assert.Equal(branchName, branch.Payload.Name);

        WhenCommand(new CreateClient(branchId, clientName, clientEmail))
            .ThenNotThrowsAnException()
            .ThenGetLatestEvents(
                events =>
                {
                    foreach (var ev in events.Where(m => m.GetPayload().GetType() == typeof(ClientCreated)))
                        FirstEventDatetime = ev.TimeStamp;
                })
            .ThenPayloadIs(new Client(branchId, clientName, clientEmail))
            .ThenSingleProjectionPayloadIs(
                new ClientNameHistoryProjection(
                    branchId,
                    new List<ClientNameHistoryProjection.ClientNameHistoryProjectionRecord>
                            { new(clientName, FirstEventDatetime) }
                        .ToImmutableList(),
                    clientEmail)
            );
    }

    [Fact]
    public void ChangeNameTest()
    {
        GivenScenario(CreateTest)
            .WhenCommand(client => new ChangeClientName(client.AggregateId, clientNameChanged)
                { ReferenceVersion = client.Version })
            .ThenNotThrowsAnException()
            .ThenGetLatestEvents(
                events =>
                {
                    foreach (var ev in events.Where(e => e.GetPayload().GetType().Name == nameof(ClientNameChanged)))
                        ChangedEventDatetime = ev.TimeStamp;
                })
            .ThenPayloadIs(new Client(branchId, clientNameChanged, clientEmail))
            .ThenSingleProjectionPayloadIs(
                new ClientNameHistoryProjection(
                    branchId,
                    new List<ClientNameHistoryProjection.ClientNameHistoryProjectionRecord>
                    {
                        new(clientName, FirstEventDatetime), new(clientNameChanged, ChangedEventDatetime)
                    },
                    clientEmail))
            .ThenAggregateQueryResponseIs<ClientEmailExistsQuery, ClientEmailExistsQuery.QueryParameter, bool>(
                new ClientEmailExistsQuery.QueryParameter(clientEmail),
                true)
            .ThenAggregateQueryResponseIs<ClientEmailExistsQuery, ClientEmailExistsQuery.QueryParameter, bool>(
                new ClientEmailExistsQuery.QueryParameter("not" + clientEmail),
                false)
            .ThenAggregateListQueryResponseIs<BasicClientQuery, BasicClientQueryParameter, BasicClientQueryModel>(
                new BasicClientQueryParameter(
                    branchId,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null),
                new ListQueryResult<BasicClientQueryModel>(
                    1,
                    null,
                    null,
                    null,
                    new[]
                    {
                        new BasicClientQueryModel(branchId, clientNameChanged, clientEmail)
                    }))
            .ThenGetSingleProjectionListQueryResponse<ClientNameHistoryProjection, ClientNameHistoryProjectionQuery,
                ClientNameHistoryProjectionQuery.Parameter,
                ClientNameHistoryProjectionQuery.Response>(
                new ClientNameHistoryProjectionQuery.Parameter(null, null, null, null, null),
                value => { });
    }

    [Fact]
    public void SingleProjectionListQueryTest()
    {
        RunEnvironmentCommand(new CreateBranch(branchName), branchId);
        WhenCommand(new CreateClient(branchId, clientName, clientEmail))
            .ThenGetLatestSingleEvent<ClientCreated>(ev => FirstEventDatetime = ev.TimeStamp)
            .WhenCommand(client => new ChangeClientName(client.AggregateId, clientNameChanged)
                { ReferenceVersion = client.Version })
            .ThenGetLatestSingleEvent<ClientNameChanged>(ev => ChangedEventDatetime = ev.TimeStamp)
            .ThenSingleProjectionListQueryResponseIs<ClientNameHistoryProjection, ClientNameHistoryProjectionQuery,
                ClientNameHistoryProjectionQuery.Parameter, ClientNameHistoryProjectionQuery.Response>(
                new ClientNameHistoryProjectionQuery.Parameter(null, null, branchId, null, null),
                new ListQueryResult<ClientNameHistoryProjectionQuery.Response>(
                    2,
                    null,
                    null,
                    null,
                    new[]
                    {
                        new ClientNameHistoryProjectionQuery.Response(branchId, GetAggregateId(), clientName,
                            clientEmail, FirstEventDatetime),
                        new ClientNameHistoryProjectionQuery.Response(
                            branchId,
                            GetAggregateId(),
                            clientNameChanged,
                            clientEmail,
                            ChangedEventDatetime)
                    }));
    }

    [Fact]
    public void SingleProjectionQueryTest()
    {
        RunEnvironmentCommand(new CreateBranch(branchName), branchId);
        WhenCommand(new CreateClient(branchId, clientName, clientEmail))
            .ThenGetLatestSingleEvent<ClientCreated>(ev => FirstEventDatetime = ev.TimeStamp)
            .WhenCommand(client => new ChangeClientName(client.AggregateId, clientNameChanged)
                { ReferenceVersion = client.Version })
            .ThenGetLatestSingleEvent<ClientNameChanged>(ev => ChangedEventDatetime = ev.TimeStamp)
            .ThenSingleProjectionQueryResponseIs<ClientNameHistoryProjection, ClientNameHistoryProjectionCountQuery,
                ClientNameHistoryProjectionCountQuery.Parameter, int>(
                new ClientNameHistoryProjectionCountQuery.Parameter(branchId, GetAggregateId()),
                2);
    }

    [Fact]
    public void CheckGetAllAggregateEvent()
    {
        RunEnvironmentCommand(new CreateBranch(branchName), branchId);
        WhenCommand(new CreateClient(branchId, clientName, clientEmail))
            .WhenCommand(new ChangeClientName(GetAggregateId(), clientNameChanged)
                { ReferenceVersion = GetCurrentVersion() })
            .ThenGetAllAggregateEvents(
                events =>
                {
                    Assert.Equal(2, events.Count);
                    Assert.Equal(typeof(ClientCreated), events[0].GetPayload().GetType());
                    Assert.Equal(typeof(ClientNameChanged), events[1].GetPayload().GetType());
                });
    }

    [Fact]
    public void TestWithFile()
    {
        // Sekibanのテストでは結果をファイルに書いたり、期待値をファイルから読み込んだり、JSONで比較したりすることができる。
        GivenScenario(ChangeNameTest)
            .WriteStateToFile("ClientTestOut.json")
            .WritePayloadToFile("ClientContentsTestOut.json")
            .ThenStateIsFromJson(
                "{\"Payload\":{\"BranchId\":\"cdb93f86-8d2f-442c-9f62-b9e791401f5f\",\"ClientName\":\"Test Client Changed\",\"ClientEmail\":\"client@example.com\"},\"IsDeleted\":false,\"AggregateId\":\"9cfa698b-fda7-44a1-86c0-1f167914bb47\",\"Version\":2,\"LastEventId\":\"19c7e148-550f-4954-b0d5-e05ef93cb32a\",\"AppliedSnapshotVersion\":0,\"LastSortableUniqueId\":\"638002628133105260000616586510\"}");

        WriteStateToFile("TEMPTEST.json");

        ThenStateIsFromFile("ClientTestResult.json")
            .ThenPayloadIsFromJson(
                "{\"BranchId\":\"cdb93f86-8d2f-442c-9f62-b9e791401f5f\",\"ClientName\":\"Test Client Changed\",\"ClientEmail\":\"client@example.com\"}")
            .ThenPayloadIsFromFile("ClientContentsTestResult.json")
            .WriteSingleProjectionStateToFile<ClientNameHistoryProjection>("ClientProjectionOut.json")
            .ThenSingleProjectionPayloadIsFromJson<ClientNameHistoryProjection>(
                "{\"BranchId\":\"cdb93f86-8d2f-442c-9f62-b9e791401f5f\",\"ClientNames\":[{\"Name\":\"Test Client\",\"DateChanged\":\"" +
                FirstEventDatetime.ToString("O") +
                "\"},{\"Name\":\"Test Client Changed\",\"DateChanged\":\"" +
                ChangedEventDatetime.ToString("O") +
                "\"}],\"ClientEmail\":\"client@example.com\"}");
    }
}
