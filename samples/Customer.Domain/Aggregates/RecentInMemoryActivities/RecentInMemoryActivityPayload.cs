using Sekiban.Core.Aggregate;
namespace Customer.Domain.Aggregates.RecentInMemoryActivities;

public record RecentInMemoryActivityPayload : IAggregatePayload
{
    public List<RecentInMemoryActivityRecord> LatestActivities { get; set; } = new();
    public RecentInMemoryActivityPayload(List<RecentInMemoryActivityRecord> latestActivities)
    {
        LatestActivities = latestActivities;
    }
    public RecentInMemoryActivityPayload() { }
}
