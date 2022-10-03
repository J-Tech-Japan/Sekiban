using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Aggregates.Clients.Projections;
using CustomerDomainContext.Shared;
using Sekiban.EventSourcing.TestHelpers;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
namespace CustomerDomainXTest.AggregateTests;

public class ClientAndProjectionSpec : SingleAggregateTestBase<Client, ClientContents>
{
    public readonly string branchName = "BranchName";
    public readonly string clientEmail = "client@example.com";
    public readonly string clientName = "Test Client";
    public readonly string clientNameChanged = "Test Client Changed";
    public Guid branchId = Guid.Parse("cdb93f86-8d2f-442c-9f62-b9e791401f5f");
    public DateTime FirstEventDatetime { get; set; } = DateTime.Now;
    public DateTime ChangedEventDatetime { get; set; } = DateTime.Now;
    public SingleProjectionTestEventSubscriber<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.ContentsDefinition>
        ProjectionSubscriber
    {
        get;
    } = new();
    public ClientAndProjectionSpec() : base(CustomerDependency.GetOptions())
    {
    }

    [Fact]
    public void CreateTest()
    {
        GivenEventSubscriber(ProjectionSubscriber)
            .GivenEnvironmentDtoContents<Branch, BranchContents>(branchId, new BranchContents { Name = branchName })
            .WhenCreate(new CreateClient(branchId, clientName, clientEmail))
            .ThenEvents(
                events =>
                {
                    foreach (var ev in events.Where(m => m.GetPayload().GetType() == typeof(ClientCreated)))
                    {
                        FirstEventDatetime = ev.TimeStamp;
                    }
                })
            .ThenContents(new ClientContents(branchId, clientName, clientEmail));
        ProjectionSubscriber.ThenContents(
            new ClientNameHistoryProjection.ContentsDefinition(
                branchId,
                new List<ClientNameHistoryProjection.ClientNameHistoryProjectionRecord> { new(clientName, FirstEventDatetime) },
                clientEmail));
    }
    [Fact]
    public void ChangeNameTest()
    {
        GivenScenario(CreateTest)
            .WhenChange(client => new ChangeClientName(client.AggregateId, clientNameChanged) { ReferenceVersion = client.Version })
            .ThenNotThrowsAnException()
            .ThenEvents(
                events =>
                {
                    foreach (var ev in events.Where(e => e.GetPayload().GetType().Name == nameof(ClientNameChanged)))
                    {
                        ChangedEventDatetime = ev.TimeStamp;
                    }
                })
            .ThenContents(new ClientContents(branchId, clientNameChanged, clientEmail));
        ProjectionSubscriber.ThenContents(
            new ClientNameHistoryProjection.ContentsDefinition(
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
            .WriteDtoToFile("ClientTestOut.json")
            .WriteContentsToFile("ClientContentsTestOut.json")
            .ThenStateFromJson(
                "{\"Contents\":{\"BranchId\":\"cdb93f86-8d2f-442c-9f62-b9e791401f5f\",\"ClientName\":\"Test Client Changed\",\"ClientEmail\":\"client@example.com\"},\"IsDeleted\":false,\"AggregateId\":\"9cfa698b-fda7-44a1-86c0-1f167914bb47\",\"Version\":2,\"LastEventId\":\"19c7e148-550f-4954-b0d5-e05ef93cb32a\",\"AppliedSnapshotVersion\":0,\"LastSortableUniqueId\":\"638002628133105260000616586510\"}")
            .ThenStateFromFile("ClientTestResult.json")
            .ThenContentsFromJson(
                "{\"BranchId\":\"cdb93f86-8d2f-442c-9f62-b9e791401f5f\",\"ClientName\":\"Test Client Changed\",\"ClientEmail\":\"client@example.com\"}")
            .ThenContentsFromFile("ClientContentsTestResult.json");
        ProjectionSubscriber.WriteProjectionDto("ClientProjectionOut.json")
            .ThenContentsFromJson(
                "{\"BranchId\":\"cdb93f86-8d2f-442c-9f62-b9e791401f5f\",\"ClientNames\":[{\"Name\":\"Test Client\",\"DateChanged\":\"" +
                FirstEventDatetime.ToString("O") +
                "\"},{\"Name\":\"Test Client Changed\",\"DateChanged\":\"" +
                ChangedEventDatetime.ToString("O") +
                "\"}],\"ClientEmail\":\"client@example.com\"}");
    }
}
