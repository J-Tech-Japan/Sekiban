using CustomerDomainContext.Aggregates.RecentActivities.Events;
namespace CustomerDomainContext.Aggregates.RecentActivities;

[AggregateContainerGroup(AggregateContainerGroup.Dissolvable)]
public class RecentActivity : TransferableAggregateBase<RecentActivityDto>
{
    private List<RecentActivityRecord> LatestActivities { get; set; } = new();

    public RecentActivity(Guid aggregateId) : base(aggregateId) { }

    public RecentActivity(Guid aggregateId, string firstActivity) : base(aggregateId)
    {
        AddAndApplyEvent(new RecentActivityCreated(aggregateId, new RecentActivityRecord(firstActivity, DateTime.UtcNow)));
    }

    protected override Action? GetApplyEventAction(AggregateEvent ev) =>
        ev switch
        {
            RecentActivityCreated created => () =>
            {
                LatestActivities.Add(created.Activity);
            },
            RecentActivityAdded added => () =>
            {
                LatestActivities.Insert(0, added.Record);
                if (LatestActivities.Count > 5)
                {
                    LatestActivities.RemoveRange(5, LatestActivities.Count - 5);
                }
            },
            _ => null
        };
    public override RecentActivityDto ToDto() =>
        new(this) { LatestActivities = LatestActivities };
    protected override void CopyPropertiesFromSnapshot(RecentActivityDto snapshot)
    {
        LatestActivities = snapshot.LatestActivities;
    }
    public void AddActivity(string activity)
    {
        var ev = new RecentActivityAdded(AggregateId, new RecentActivityRecord(activity, DateTime.UtcNow));
        AddAndApplyEvent(ev);
    }
}
