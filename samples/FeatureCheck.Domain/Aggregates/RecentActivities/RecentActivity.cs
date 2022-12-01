using System.Collections.Immutable;
using Sekiban.Core.Aggregate;

namespace Customer.Domain.Aggregates.RecentActivities;

[AggregateContainerGroup(AggregateContainerGroup.Dissolvable)]
public record RecentActivity(ImmutableList<RecentActivityRecord> LatestActivities) : IAggregatePayload
{
    public RecentActivity() : this(ImmutableList<RecentActivityRecord>.Empty)
    {
    }
}
