using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Snapshot.Aggregate.Events;
namespace Sekiban.Core.Snapshot.Aggregate;

[AggregateContainerGroup(AggregateContainerGroup.InMemoryContainer)]
public class SnapshotManager : AggregateBase<SnapshotManagerContents>
{
    private const int SnapshotCount = 40;
    private const int SnapshotTakeOffset = 15;
    public static Guid SharedId { get; } = Guid.NewGuid();
    public void Created(DateTime createdAt)
    {
        AddAndApplyEvent(new SnapshotManagerCreated(createdAt));
    }
    public void ReportAggregateVersion(
        Type aggregateType,
        Guid targetAggregateId,
        int version,
        int? snapshotVersion,
        int snapshotFrequency = SnapshotCount,
        int snapshotOffset = SnapshotTakeOffset)
    {
        var nextSnapshotVersion = version / snapshotFrequency * snapshotFrequency;
        var offset = version - nextSnapshotVersion;
        if (nextSnapshotVersion == 0) { return; }
        var key = SnapshotKey(aggregateType.Name, targetAggregateId, nextSnapshotVersion);
        if (!Contents.Requests.Contains(key) && !Contents.RequestTakens.Contains(key))
        {
            AddAndApplyEvent(new SnapshotManagerRequestAdded(aggregateType.Name, targetAggregateId, nextSnapshotVersion, snapshotVersion));
        }
        if (Contents.Requests.Contains(key) && !Contents.RequestTakens.Contains(key) && offset > snapshotOffset)
        {
            AddAndApplyEvent(new SnapshotManagerSnapshotTaken(aggregateType.Name, targetAggregateId, nextSnapshotVersion, snapshotVersion));
        }
    }
    private static string SnapshotKey(string aggregateTypeName, Guid targetAggregateId, int nextSnapshotVersion)
    {
        return $"{aggregateTypeName}_{targetAggregateId.ToString()}_{nextSnapshotVersion}";
    }
    protected override Func<AggregateVariable<SnapshotManagerContents>, AggregateVariable<SnapshotManagerContents>>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload)
    {
        return payload switch
        {
            SnapshotManagerCreated created => variable => new AggregateVariable<SnapshotManagerContents>(new SnapshotManagerContents()),
            SnapshotManagerRequestAdded requestAdded => variable =>
            {
                var requests = variable.Contents.Requests.ToList();
                requests.Add(SnapshotKey(requestAdded.AggregateTypeName, requestAdded.TargetAggregateId, requestAdded.NextSnapshotVersion));
                return variable with { Contents = Contents with { Requests = requests } };
            },
            SnapshotManagerSnapshotTaken requestAdded => variable =>
            {
                var requests = Contents.Requests.ToList();
                var requestTakens = Contents.RequestTakens.ToList();
                requests.Remove(SnapshotKey(requestAdded.AggregateTypeName, requestAdded.TargetAggregateId, requestAdded.NextSnapshotVersion));
                requestTakens.Add(SnapshotKey(requestAdded.AggregateTypeName, requestAdded.TargetAggregateId, requestAdded.NextSnapshotVersion));
                return variable with { Contents = Contents with { Requests = requests, RequestTakens = requestTakens } };
            },
            _ => null
        };
    }
}
