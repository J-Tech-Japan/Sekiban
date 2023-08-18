using FeatureCheck.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Projections;

public record TenRecentProjection : ISingleProjectionPayload<RecentActivity, TenRecentProjection>
{
    public ImmutableList<RecentActivityRecord> List { get; init; } = ImmutableList<RecentActivityRecord>.Empty;
    public static TenRecentProjection? ApplyEvent<TEventPayload>(TenRecentProjection projectionPayload, Event<TEventPayload> ev)
        where TEventPayload : IEventPayloadCommon =>
        ev.Payload switch
        {
            RecentActivityAdded recentActivityAdded => projectionPayload with
            {
                List = projectionPayload.List.Add(recentActivityAdded.Record).Take(10).ToImmutableList()
            },
            RecentActivityCreated recentActivityCreated => projectionPayload with
            {
                List = projectionPayload.List.Add(recentActivityCreated.Activity).Take(10).ToImmutableList()
            },
            _ => null
        };
    public static TenRecentProjection CreateInitialPayload() => new();
}
