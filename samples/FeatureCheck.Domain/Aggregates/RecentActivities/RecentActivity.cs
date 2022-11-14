using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace Customer.Domain.Aggregates.RecentActivities;

[AggregateContainerGroup(AggregateContainerGroup.Dissolvable)]
public record RecentActivity(ImmutableList<RecentActivityRecord> LatestActivities) : IAggregatePayload
{
    public RecentActivity() : this(ImmutableList<RecentActivityRecord>.Empty) { }
}
