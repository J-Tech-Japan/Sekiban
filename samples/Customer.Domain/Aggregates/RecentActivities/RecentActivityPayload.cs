using Sekiban.Core.Aggregate;
namespace Customer.Domain.Aggregates.RecentActivities;

public record RecentActivityPayload : IAggregatePayload
{
    public IReadOnlyCollection<RecentActivityRecord> LatestActivities { get; set; } = new List<RecentActivityRecord> { new() };
    public RecentActivityPayload(IReadOnlyCollection<RecentActivityRecord> latestActivities)
    {
        LatestActivities = latestActivities;
    }
    public RecentActivityPayload() { }
    public virtual bool Equals(RecentActivityPayload? other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }
        if (ReferenceEquals(this, other))
        {
            return true;
        }
        return LatestActivities.SequenceEqual(other.LatestActivities);
    }
    public override int GetHashCode()
    {
        return LatestActivities.GetHashCode();
    }
}
