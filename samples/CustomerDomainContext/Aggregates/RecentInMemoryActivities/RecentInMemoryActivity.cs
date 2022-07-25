using CustomerDomainContext.Aggregates.RecentInMemoryActivities.Events;
namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities;

[AggregateContainerGroup(AggregateContainerGroup.InMemoryContainer)]
public class RecentInMemoryActivity : TransferableAggregateBase<RecentInMemoryActivityContents>
{

    public void CreateRecentInMemoryActivity(string firstActivity)
    {
        AddAndApplyEvent(new RecentInMemoryActivityCreated(new RecentInMemoryActivityRecord(firstActivity, DateTime.UtcNow)));
    }

    protected override Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload) =>
        payload switch
        {
            RecentInMemoryActivityCreated created => () =>
            {
                Contents = new RecentInMemoryActivityContents(new List<RecentInMemoryActivityRecord> { created.Activity });
            },
            RecentInMemoryActivityAdded added => () =>
            {
                var records = Contents.LatestActivities.ToList();
                records.Insert(0, added.Record);
                if (records.Count > 5)
                {
                    records.RemoveRange(5, records.Count - 5);
                }
                Contents = new RecentInMemoryActivityContents(records);
            },
            _ => null
        };
    public void AddActivity(string activity)
    {
        var ev = new RecentInMemoryActivityAdded(new RecentInMemoryActivityRecord(activity, DateTime.UtcNow));
        AddAndApplyEvent(ev);
    }
}
