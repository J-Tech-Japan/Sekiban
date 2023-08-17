using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentActivities;

[AggregateContainerGroup(AggregateContainerGroup.Dissolvable)]
public record RecentActivity(ImmutableList<RecentActivityRecord> LatestActivities) : IAggregatePayload
{
    public string GetPayloadVersionIdentifier() => "1.0.1 20230101";
    public static IAggregatePayloadCommon CreateInitialPayload() => new RecentActivity(ImmutableList<RecentActivityRecord>.Empty);
}
