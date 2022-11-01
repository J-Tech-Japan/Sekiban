using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.Clients.Projections;
using Customer.Domain.Shared;
using Sekiban.Testing.SingleAggregate;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;
namespace Customer.Test.AggregateTests;

public class ClientAndProjectionSpec : SingleAggregateTestBase<Client, CustomerDependency>
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
        RunEnvironmentCreateCommand(new CreateBranch(branchName), branchId);
        // GetEnvironmentAggregateStateのテスト
        var branch = GetEnvironmentAggregateState<Branch>(branchId);
        Assert.Equal(branchName, branch.Payload.Name);

        WhenCreate(new CreateClient(branchId, clientName, clientEmail))
            .ThenNotThrowsAnException()
            .ThenGetEvents(
                events =>
                {
                    foreach (var ev in events.Where(m => m.GetPayload().GetType() == typeof(ClientCreated)))
                    {
                        FirstEventDatetime = ev.TimeStamp;
                    }
                })
            .ThenPayloadIs(new Client(branchId, clientName, clientEmail))
            .ThenGetSingleProjectionTest<ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>(
                test =>
                    test.ThenPayloadIs(
                        new ClientNameHistoryProjection.PayloadDefinition(
                            branchId,
                            new List<ClientNameHistoryProjection.ClientNameHistoryProjectionRecord> { new(clientName, FirstEventDatetime) }
                                .ToImmutableList(),
                            clientEmail))
            );
    }
    [Fact]
    public void ChangeNameTest()
    {
        GivenScenario(CreateTest)
            .WhenChange(client => new ChangeClientName(client.AggregateId, clientNameChanged) { ReferenceVersion = client.Version })
            .ThenNotThrowsAnException()
            .ThenGetEvents(
                events =>
                {
                    foreach (var ev in events.Where(e => e.GetPayload().GetType().Name == nameof(ClientNameChanged)))
                    {
                        ChangedEventDatetime = ev.TimeStamp;
                    }
                })
            .ThenPayloadIs(new Client(branchId, clientNameChanged, clientEmail))
            .ThenGetSingleProjectionTest<ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>(
                test =>
                    test.ThenPayloadIs(
                        new ClientNameHistoryProjection.PayloadDefinition(
                            branchId,
                            new List<ClientNameHistoryProjection.ClientNameHistoryProjectionRecord>
                            {
                                new(clientName, FirstEventDatetime), new(clientNameChanged, ChangedEventDatetime)
                            },
                            clientEmail)));
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
            .ThenGetSingleProjectionTest<ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>(
                test => test.WriteProjectionStateToFile("ClientProjectionOut.json")
                    .ThenPayloadIsFromJson(
                        "{\"BranchId\":\"cdb93f86-8d2f-442c-9f62-b9e791401f5f\",\"ClientNames\":[{\"Name\":\"Test Client\",\"DateChanged\":\"" +
                        FirstEventDatetime.ToString("O") +
                        "\"},{\"Name\":\"Test Client Changed\",\"DateChanged\":\"" +
                        ChangedEventDatetime.ToString("O") +
                        "\"}],\"ClientEmail\":\"client@example.com\"}")
            );
    }
}
