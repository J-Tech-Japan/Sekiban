using CustomerWithTenantAddonDomainContext.Aggregates.RecentActivities.Events;
namespace CustomerWithTenantAddonDomainContext.Aggregates.RecentActivities;

[AggregateContainerGroup(AggregateContainerGroup.Dissolvable)]
public class RecentActivity : TransferableAggregateBase<RecentActivityContents>
{

    public void CreateRecentActivity(string firstActivity)
    {
        AddAndApplyEvent(new RecentActivityCreated(new RecentActivityRecord(firstActivity, DateTime.UtcNow)));
    }

    protected override Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload) =>
        payload switch
        {
            RecentActivityCreated created => () =>
            {
                Contents = new RecentActivityContents(new List<RecentActivityRecord> { created.Activity });
            },
            RecentActivityAdded added => () =>
            {
                var records = Contents.LatestActivities.ToList();
                records.Insert(0, added.Record);
                if (records.Count > 5)
                {
                    records.RemoveRange(5, records.Count - 5);
                }
                Contents = new RecentActivityContents(records);
            },
            _ => null
        };
    public void AddActivity(string activity)
    {
        var ev = new RecentActivityAdded(new RecentActivityRecord(activity, DateTime.UtcNow));
        AddAndApplyEvent(ev);
    }
}