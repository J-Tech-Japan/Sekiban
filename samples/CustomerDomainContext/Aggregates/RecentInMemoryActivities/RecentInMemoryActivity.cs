using CustomerDomainContext.Aggregates.RecentActivities;
using CustomerDomainContext.Aggregates.RecentActivities.Events;
using CustomerDomainContext.Aggregates.RecentInMemoryActivities.Events;
namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities;

[AggregateContainerGroup(AggregateContainerGroup.InMemoryContainer)]
public class RecentInMemoryActivity : TransferableAggregateBase<RecentInMemoryActivityDto>
{
    private List<RecentInMemoryActivityRecord> LatestActivities { get; set; }= new List<RecentInMemoryActivityRecord>();

    public RecentInMemoryActivity(Guid aggregateId) : base(aggregateId) { }

    public RecentInMemoryActivity(Guid aggregateId, string firstActivity) : base(aggregateId)
    {
        AddAndApplyEvent(new RecentInMemoryActivityCreated(aggregateId, new RecentInMemoryActivityRecord(firstActivity, DateTime.UtcNow)));
    }
    protected sealed override void AddAndApplyEvent(AggregateEvent ev)
    {
        base.AddAndApplyEvent(ev);
    }

    protected override Action? GetApplyEventAction(AggregateEvent ev) => ev switch
    {
        RecentInMemoryActivityCreated created => () =>
        {
            LatestActivities.Add(created.Activity);
        },
        RecentInMemoryActivityAdded added => () =>
        {
            LatestActivities.Insert(0, added.Record);
            if (LatestActivities.Count > 5)
            {
                LatestActivities.RemoveRange(5, LatestActivities.Count - 5);
            }
        },
        _ => null
    };
    public override RecentInMemoryActivityDto ToDto() => new(this)
    {
        LatestActivities = LatestActivities
    };
    protected override void CopyPropertiesFromSnapshot(RecentInMemoryActivityDto snapshot)
    {
        LatestActivities = snapshot.LatestActivities;
    }
    public void AddActivity(string activity)
    {
        var ev = new RecentInMemoryActivityAdded(AggregateId,new RecentInMemoryActivityRecord(activity, DateTime.UtcNow));
        AddAndApplyEvent(ev);
    }
}
