namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities
{
    public record RecentInMemoryActivityContents : IAggregateContents
    {
        public List<RecentInMemoryActivityRecord> LatestActivities { get; set; } = new();
        public RecentInMemoryActivityContents(List<RecentInMemoryActivityRecord> latestActivities) =>
            LatestActivities = latestActivities;
        public RecentInMemoryActivityContents() { }
    }
}
