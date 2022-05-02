namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities;

public record RecentInMemoryActivityDto : AggregateDtoBase
{
    public List<RecentInMemoryActivityRecord> LatestActivities { get; set; } = new();

    public RecentInMemoryActivityDto() { }
    public RecentInMemoryActivityDto(RecentInMemoryActivity aggregate) : base(aggregate) { }
}
