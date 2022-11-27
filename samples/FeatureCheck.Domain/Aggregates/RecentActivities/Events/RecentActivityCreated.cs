using Sekiban.Core.Event;
using System.Collections.Immutable;
namespace Customer.Domain.Aggregates.RecentActivities.Events;

public record RecentActivityCreated(RecentActivityRecord Activity) : IApplicableEvent<RecentActivity>
{
    public RecentActivity OnEvent(RecentActivity payload, IEvent ev) => new RecentActivity(ImmutableList<RecentActivityRecord>.Empty.Add(Activity));
}
