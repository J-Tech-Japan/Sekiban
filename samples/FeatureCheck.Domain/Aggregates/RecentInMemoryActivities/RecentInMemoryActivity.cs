using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentInMemoryActivities;

[AggregateContainerGroup(AggregateContainerGroup.InMemory)]
public record RecentInMemoryActivity(ImmutableList<RecentInMemoryActivityRecord> LatestActivities) : IAggregatePayload
{
    public static IAggregatePayloadCommon CreateInitialPayload() => new RecentInMemoryActivity(ImmutableList<RecentInMemoryActivityRecord>.Empty);
}
