using FeatureCheck.Domain.Aggregates.RecentActivities;
using FeatureCheck.Domain.Aggregates.RecentActivities.Commands;
using FeatureCheck.Domain.Aggregates.RecentActivities.Events;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class RecentActivityTest : AggregateTest<RecentActivity, FeatureCheckDependency>
{
    private RecentActivityRecord firstRecord = new("first", DateTime.UtcNow);
    private RecentActivityRecord publishOnlyRecord = new("publish only", DateTime.UtcNow);
    private RecentActivityRecord regularRecord = new("regular", DateTime.UtcNow);

    [Fact]
    public void CreateRecentActivityTest()
    {
        WhenCommand(new CreateRecentActivity())
            .ThenNotThrowsAnException()
            .ThenGetLatestSingleEvent<RecentActivityCreated>(ev => firstRecord = ev.Payload.Activity)
            .ThenPayloadIs(new RecentActivity(new List<RecentActivityRecord> { firstRecord }.ToImmutableList()));
    }

    [Fact]
    public void AddRegularEventTest()
    {
        GivenScenario(CreateRecentActivityTest)
            .WhenCommand(new AddRecentActivity(GetAggregateId(), "Regular Event"))
            .ThenNotThrowsAnException()
            .ThenGetLatestSingleEvent<RecentActivityAdded>(ev => regularRecord = ev.Payload.Record)
            .ThenPayloadIs(
                new RecentActivity(
                    new List<RecentActivityRecord> { regularRecord, firstRecord }
                        .ToImmutableList()));
    }

    [Fact]
    public void PublishOnlyCommandTest()
    {
        GivenScenario(AddRegularEventTest)
            .WhenCommand(new OnlyPublishingAddRecentActivity(GetAggregateId(), "Publish Only Event"))
            .ThenNotThrowsAnException()
            .ThenGetLatestSingleEvent<RecentActivityAdded>(ev => publishOnlyRecord = ev.Payload.Record)
            .ThenPayloadIs(
                new RecentActivity(
                    new List<RecentActivityRecord>
                        { publishOnlyRecord, regularRecord, firstRecord }.ToImmutableList()));
    }
}
