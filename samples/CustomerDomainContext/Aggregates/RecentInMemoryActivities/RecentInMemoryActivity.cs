using CustomerDomainContext.Aggregates.RecentInMemoryActivities.Events;
namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities;

[AggregateContainerGroup(AggregateContainerGroup.InMemoryContainer)]
public class RecentInMemoryActivity : AggregateBase<RecentInMemoryActivityContents>
{

    public void CreateRecentInMemoryActivity(string firstActivity)
    {
        AddAndApplyEvent(new RecentInMemoryActivityCreated(new RecentInMemoryActivityRecord(firstActivity, DateTime.UtcNow)));
    }
    protected override Func<AggregateVariable<RecentInMemoryActivityContents>, AggregateVariable<RecentInMemoryActivityContents>>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload)
    {
        return payload switch
        {
            RecentInMemoryActivityCreated created => _ =>
                new AggregateVariable<RecentInMemoryActivityContents>(
                    new RecentInMemoryActivityContents(new List<RecentInMemoryActivityRecord> { created.Activity })),
            RecentInMemoryActivityAdded added => variable =>
            {
                var records = variable.Contents.LatestActivities.ToList();
                records.Insert(0, added.Record);
                if (records.Count > 5)
                {
                    records.RemoveRange(5, records.Count - 5);
                }
                return variable with { Contents = new RecentInMemoryActivityContents(records) };
            },
            _ => null
        };
    }
    public void AddActivity(string activity)
    {
        var ev = new RecentInMemoryActivityAdded(new RecentInMemoryActivityRecord(activity, DateTime.UtcNow));
        AddAndApplyEvent(ev);
    }
}
