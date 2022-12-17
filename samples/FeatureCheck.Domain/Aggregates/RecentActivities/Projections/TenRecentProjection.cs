using FeatureCheck.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Event;
using Sekiban.Core.Query.SingleProjections;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Projections;

public record TenRecentProjection : ISingleProjectionPayload<RecentActivity, TenRecentProjection>
{
    public ImmutableList<RecentActivityRecord> List { get; init; } = ImmutableList<RecentActivityRecord>.Empty;
    public Func<TenRecentProjection, TenRecentProjection>? GetApplyEventFunc(IEvent ev, IEventPayloadCommon eventPayload) => eventPayload switch
    {
        RecentActivityAdded recentActivityAdded => p => p with { List = p.List.Add(recentActivityAdded.Record).Take(10).ToImmutableList() },
        RecentActivityCreated recentActivityCreated => p => p with { List = p.List.Add(recentActivityCreated.Activity).Take(10).ToImmutableList() },
        _ => null
    };
}
