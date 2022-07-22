namespace CustomerDomainContext.Aggregates.RecentActivities;

public record RecentActivityContents : IAggregateContents
{
    public IReadOnlyCollection<RecentActivityRecord> LatestActivities { get; set; } = new List<RecentActivityRecord>();
    public RecentActivityContents(IReadOnlyCollection<RecentActivityRecord> latestActivities) =>
        LatestActivities = latestActivities;
    public virtual bool Equals(RecentActivityContents? other)
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
