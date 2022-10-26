using Customer.Domain.Aggregates.RecentActivities;
using Customer.Domain.Aggregates.RecentActivities.Commands;
using Customer.Domain.Aggregates.RecentActivities.Events;
using Customer.Domain.Shared;
using Sekiban.Core.Event;
using Sekiban.Testing.SingleAggregate;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Xunit;
namespace Customer.Test.AggregateTests;

public class RecentActivityTest : SingleAggregateTestBase<RecentActivity, CustomerDependency>
{
    private RecentActivityRecord firstRecord = new("first", DateTime.UtcNow);
    private RecentActivityRecord publishOnlyRecord = new("publish only", DateTime.UtcNow);
    private RecentActivityRecord regularRecord = new("regular", DateTime.UtcNow);
    [Fact]
    public void CreateRecentActivityTest()
    {
        WhenCreate(new CreateRecentActivity())
            .ThenNotThrowsAnException()
            .ThenGetSingleEvent<AggregateEvent<RecentActivityCreated>>(ev => firstRecord = ev.Payload.Activity)
            .ThenPayloadIs(new RecentActivity(new List<RecentActivityRecord> { firstRecord }.ToImmutableList()));
    }
    [Fact]
    public void AddRegularEventTest()
    {
        GivenScenario(CreateRecentActivityTest)
            .WhenChange(new AddRecentActivity(GetAggregateId(), "Regular Event"))
            .ThenNotThrowsAnException()
            .ThenGetSingleEvent<AggregateEvent<RecentActivityAdded>>(ev => regularRecord = ev.Payload.Record)
            .ThenPayloadIs(new RecentActivity(new List<RecentActivityRecord> { regularRecord, firstRecord }.ToImmutableList()));
    }
    [Fact]
    public void PublishOnlyCommandTest()
    {
        GivenScenario(AddRegularEventTest)
            .WhenChange(new OnlyPublishingAddRecentActivity(GetAggregateId(), "Publish Only Event"))
            .ThenNotThrowsAnException()
            .ThenGetSingleEvent<AggregateEvent<RecentActivityAdded>>(ev => publishOnlyRecord = ev.Payload.Record)
            .ThenPayloadIs(new RecentActivity(new List<RecentActivityRecord> { publishOnlyRecord, regularRecord, firstRecord }.ToImmutableList()));
    }
}
