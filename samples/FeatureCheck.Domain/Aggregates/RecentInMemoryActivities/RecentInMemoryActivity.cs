using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentInMemoryActivities;

[AggregateContainerGroup(AggregateContainerGroup.InMemory)]
public record RecentInMemoryActivity(ImmutableList<RecentInMemoryActivityRecord> LatestActivities) : IAggregatePayload<RecentInMemoryActivity>
{
    public static RecentInMemoryActivity CreateInitialPayload(RecentInMemoryActivity? _) => new(ImmutableList<RecentInMemoryActivityRecord>.Empty);
}
