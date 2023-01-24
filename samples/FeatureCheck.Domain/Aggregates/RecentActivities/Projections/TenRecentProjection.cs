using FeatureCheck.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Projections;

public record TenRecentProjection : ISingleProjectionPayload<RecentActivity, TenRecentProjection>
{
    public ImmutableList<RecentActivityRecord> List { get; init; } = ImmutableList<RecentActivityRecord>.Empty;
    public static Func<TenRecentProjection, TenRecentProjection>? GetApplyEventFunc<TEventPayload>(Event<TEventPayload> ev)
        where TEventPayload : IEventPayloadCommon =>
        ev.Payload switch
        {
            RecentActivityAdded recentActivityAdded => p => p with { List = p.List.Add(recentActivityAdded.Record).Take(10).ToImmutableList() },
            RecentActivityCreated recentActivityCreated => p =>
                p with { List = p.List.Add(recentActivityCreated.Activity).Take(10).ToImmutableList() },
            _ => null
        };
    public Func<TenRecentProjection, TenRecentProjection>? GetApplyEventFuncInstance<TEventPayload>(Event<TEventPayload> ev)
        where TEventPayload : IEventPayloadCommon =>
        GetApplyEventFunc(ev);
}
