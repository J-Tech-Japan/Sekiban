namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities;

public record RecentInMemoryActivityContents : IAggregateContents
{
    public List<RecentInMemoryActivityRecord> LatestActivities { get; set; } = new();
    public RecentInMemoryActivityContents(List<RecentInMemoryActivityRecord> latestActivities) =>
        LatestActivities = latestActivities;
    public virtual bool Equals(RecentInMemoryActivityContents? other)
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
    public override int GetHashCode() =>
        LatestActivities.GetHashCode();
}
