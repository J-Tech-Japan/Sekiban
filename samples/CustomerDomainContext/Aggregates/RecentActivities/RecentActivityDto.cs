namespace CustomerDomainContext.Aggregates.RecentActivities;

public record RecentActivityDto : AggregateDtoBase
{
    public List<RecentActivityRecord> LatestActivities { get; set; } = new();

    public RecentActivityDto() { }
    public RecentActivityDto(RecentActivity aggregate) : base(aggregate) { }
}
