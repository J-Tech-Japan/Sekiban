namespace Customer.Domain.Aggregates.RecentActivities;

public record RecentActivityRecord(string Activity, DateTime OccuredAt)
{
    public RecentActivityRecord() : this(string.Empty, DateTime.MinValue) { }
}
