using Sekiban.Core.Events;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Events;

public record RecentInMemoryActivityCreated
    (RecentInMemoryActivityRecord Activity) : IEventPayload<RecentInMemoryActivity>
{
    public RecentInMemoryActivity OnEvent(RecentInMemoryActivity payload, IEvent ev) =>
        new RecentInMemoryActivity(ImmutableList<RecentInMemoryActivityRecord>.Empty.Add(Activity));
}
