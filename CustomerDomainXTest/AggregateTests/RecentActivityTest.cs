using CustomerDomainContext.Aggregates.RecentActivities;
using CustomerDomainContext.Aggregates.RecentActivities.Commands;
using CustomerDomainContext.Aggregates.RecentActivities.Events;
using CustomerDomainContext.Shared;
using Sekiban.Core.Event;
using Sekiban.Testing.SingleAggregate;
using System;
using System.Collections.Generic;
using Xunit;
namespace CustomerDomainXTest.AggregateTests;

public class RecentActivityTest : SingleAggregateTestBase<RecentActivity, RecentActivityContents, CustomerDependency>
{
    private RecentActivityRecord firstRecord = new("first", DateTime.UtcNow);
    private RecentActivityRecord publishOnlyRecord = new("publish only", DateTime.UtcNow);
    private RecentActivityRecord regularRecord = new("regular", DateTime.UtcNow);
    [Fact]
    public void CreateRecentActivityTest()
    {
        WhenCreate(new CreateRecentActivity())
            .ThenNotThrowsAnException()
            .ThenSingleEvent<AggregateEvent<RecentActivityCreated>>(ev => firstRecord = ev.Payload.Activity)
            .ThenContents(new RecentActivityContents(new List<RecentActivityRecord> { firstRecord }));
    }
    [Fact]
    public void AddRegularEventTest()
    {
        GivenScenario(CreateRecentActivityTest)
            .WhenChange(new AddRecentActivity(GetAggregateId(), "Regular Event"))
            .ThenNotThrowsAnException()
            .ThenSingleEvent<AggregateEvent<RecentActivityAdded>>(ev => regularRecord = ev.Payload.Record)
            .ThenContents(new RecentActivityContents(new List<RecentActivityRecord> { regularRecord, firstRecord }));
    }
    [Fact]
    public void PublishOnlyCommandTest()
    {
        GivenScenario(AddRegularEventTest)
            .WhenChange(new OnlyPublishingAddRecentActivity(GetAggregateId(), "Publish Only Event"))
            .ThenNotThrowsAnException()
            .ThenSingleEvent<AggregateEvent<RecentActivityAdded>>(ev => publishOnlyRecord = ev.Payload.Record)
            .ThenContents(new RecentActivityContents(new List<RecentActivityRecord> { publishOnlyRecord, regularRecord, firstRecord }));
    }
}
