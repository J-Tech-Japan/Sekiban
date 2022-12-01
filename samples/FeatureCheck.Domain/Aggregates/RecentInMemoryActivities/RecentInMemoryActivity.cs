using System.Collections.Immutable;
using Sekiban.Core.Aggregate;

namespace Customer.Domain.Aggregates.RecentInMemoryActivities;

[AggregateContainerGroup(AggregateContainerGroup.InMemoryContainer)]
public record RecentInMemoryActivity(ImmutableList<RecentInMemoryActivityRecord> LatestActivities) : IAggregatePayload
{
    public RecentInMemoryActivity() : this(ImmutableList<RecentInMemoryActivityRecord>.Empty)
    {
    }
}
