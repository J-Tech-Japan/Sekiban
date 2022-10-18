using CustomerDomainContext.Aggregates.RecentActivities.Events;
namespace CustomerDomainContext.Aggregates.RecentActivities;

[AggregateContainerGroup(AggregateContainerGroup.Dissolvable)]
public class RecentActivity : AggregateBase<RecentActivityContents>
{

    public void CreateRecentActivity(string firstActivity)
    {
        AddAndApplyEvent(new RecentActivityCreated(new RecentActivityRecord(firstActivity, DateTime.UtcNow)));
    }
    protected override Func<AggregateVariable<RecentActivityContents>, AggregateVariable<RecentActivityContents>>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload)
    {
        return payload switch
        {
            RecentActivityCreated created => _ =>
                new AggregateVariable<RecentActivityContents>(new RecentActivityContents(new List<RecentActivityRecord> { created.Activity })),
            RecentActivityAdded added => variable => variable with
            {
                Contents = Contents with
                {
                    LatestActivities = variable.Contents.LatestActivities.Append(added.Record)
                        .OrderByDescending(m => m.OccuredAt)
                        .Take(5)
                        .ToList()
                }
            },
            _ => null
        };
    }
    public void AddActivity(string activity)
    {
        var ev = new RecentActivityAdded(new RecentActivityRecord(activity, DateTime.UtcNow));
        AddAndApplyEvent(ev);
    }
}
