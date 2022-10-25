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
    public ClientAndProjectionSpec()
    {
        ProjectionSubscriber
            = SetupSingleAggregateProjection<SingleAggregateProjectionTestBase<Client, ClientNameHistoryProjection,
                ClientNameHistoryProjection.PayloadDefinition>>();
    }
    public DateTime FirstEventDatetime { get; set; } = DateTime.Now;
    public DateTime ChangedEventDatetime { get; set; } = DateTime.Now;
    public SingleAggregateProjectionTestBase<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>
        ProjectionSubscriber
    {
        get;
    }

    [Fact]
    public void CreateTest()
    {
        RunEnvironmentCreateCommand(new CreateBranch(branchName), branchId);
        // GetEnvironmentAggregateDtoのテスト
        var branch = GetEnvironmentAggregateDto<Branch>(branchId);
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
            .ThenContentsIs(new Client(branchId, clientName, clientEmail));
        ProjectionSubscriber.ThenContentsIs(
            new ClientNameHistoryProjection.PayloadDefinition(
                branchId,
                new List<ClientNameHistoryProjection.ClientNameHistoryProjectionRecord> { new(clientName, FirstEventDatetime) }.ToImmutableList(),
                clientEmail));
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
            .ThenContentsIs(new Client(branchId, clientNameChanged, clientEmail));
        ProjectionSubscriber.ThenContentsIs(
            new ClientNameHistoryProjection.PayloadDefinition(
                branchId,
                new List<ClientNameHistoryProjection.ClientNameHistoryProjectionRecord>
                {
                    new(clientName, FirstEventDatetime), new(clientNameChanged, ChangedEventDatetime)
                },
                clientEmail));
    }
    [Fact]
    public void TestWithFile()
    {
        // Sekibanのテストでは結果をファイルに書いたり、期待値をファイルから読み込んだり、JSONで比較したりすることができる。
        GivenScenario(ChangeNameTest)
            .WriteStateToFile("ClientTestOut.json")
            .WriteContentsToFile("ClientContentsTestOut.json")
            .ThenStateIsFromJson(
                "{\"Payload\":{\"BranchId\":\"cdb93f86-8d2f-442c-9f62-b9e791401f5f\",\"ClientName\":\"Test ClientAgggg Changed\",\"ClientEmail\":\"client@example.com\"},\"IsDeleted\":false,\"AggregateId\":\"9cfa698b-fda7-44a1-86c0-1f167914bb47\",\"Version\":2,\"LastEventId\":\"19c7e148-550f-4954-b0d5-e05ef93cb32a\",\"AppliedSnapshotVersion\":0,\"LastSortableUniqueId\":\"638002628133105260000616586510\"}")
            .ThenStateIsFromFile("ClientTestResult.json")
            .ThenContentsIsFromJson(
                "{\"BranchId\":\"cdb93f86-8d2f-442c-9f62-b9e791401f5f\",\"ClientName\":\"Test ClientAgggg Changed\",\"ClientEmail\":\"client@example.com\"}")
            .ThenContentsIsFromFile("ClientContentsTestResult.json");
        ProjectionSubscriber.WriteProjectionDtoToFile("ClientProjectionOut.json")
            .ThenContentsIsFromJson(
                "{\"BranchId\":\"cdb93f86-8d2f-442c-9f62-b9e791401f5f\",\"ClientNames\":[{\"Name\":\"Test ClientAgggg\",\"DateChanged\":\"" +
                FirstEventDatetime.ToString("O") +
                "\"},{\"Name\":\"Test ClientAgggg Changed\",\"DateChanged\":\"" +
                ChangedEventDatetime.ToString("O") +
                "\"}],\"ClientEmail\":\"client@example.com\"}");
    }
}
