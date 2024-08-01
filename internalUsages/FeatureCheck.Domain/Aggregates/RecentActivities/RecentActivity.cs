using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Aggregates.RecentActivities;

[AggregateContainerGroup(AggregateContainerGroup.Dissolvable)]
public record RecentActivity(ImmutableList<RecentActivityRecord> LatestActivities) : IAggregatePayload<RecentActivity>
{
    public static RecentActivity CreateInitialPayload(RecentActivity? _) => new(ImmutableList<RecentActivityRecord>.Empty);

    public string GetPayloadVersionIdentifier() => "1.0.1 20230101";
}
