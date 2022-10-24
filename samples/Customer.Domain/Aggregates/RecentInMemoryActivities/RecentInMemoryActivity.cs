using Customer.Domain.Aggregates.RecentInMemoryActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.RecentInMemoryActivities;

[AggregateContainerGroup(AggregateContainerGroup.InMemoryContainer)]
public class RecentInMemoryActivity : Aggregate<RecentInMemoryActivityPayload>
{

    public void CreateRecentInMemoryActivity(string firstActivity)
    {
        AddAndApplyEvent(new RecentInMemoryActivityCreated(new RecentInMemoryActivityRecord(firstActivity, DateTime.UtcNow)));
    }
    protected override Func<AggregateVariable<RecentInMemoryActivityPayload>, AggregateVariable<RecentInMemoryActivityPayload>>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload)
    {
        return payload switch
        {
            RecentInMemoryActivityCreated created => _ =>
                new AggregateVariable<RecentInMemoryActivityPayload>(
                    new RecentInMemoryActivityPayload(new List<RecentInMemoryActivityRecord> { created.Activity })),
            RecentInMemoryActivityAdded added => variable =>
            {
                var records = variable.Contents.LatestActivities.ToList();
                records.Insert(0, added.Record);
                if (records.Count > 5)
                {
                    records.RemoveRange(5, records.Count - 5);
                }
                return variable with { Contents = new RecentInMemoryActivityPayload(records) };
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
