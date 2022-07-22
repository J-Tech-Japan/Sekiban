using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Events;
namespace Sekiban.EventSourcing.Snapshots.SnapshotManagers;

[AggregateContainerGroup(AggregateContainerGroup.InMemoryContainer)]
public class SnapshotManager : TransferableAggregateBase<SnapshotManagerContents>
{
    private const int SnapshotCount = 40;
    private const int SnapshotTakeOffset = 15;
    public static Guid SharedId { get; } = Guid.NewGuid();
    public SnapshotManager(Guid aggregateId) : base(aggregateId) { }
    public SnapshotManager(Guid aggregateId, DateTime createdAt) : base(aggregateId)
    {
        AddAndApplyEvent(new SnapshotManagerCreated(createdAt));
    }
    public void ReportAggregateVersion(
        Guid snapshotManagerId,
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
    private static string SnapshotKey(string aggregateTypeName, Guid targetAggregateId, int nextSnapshotVersion) =>
        $"{aggregateTypeName}_{targetAggregateId.ToString()}_{nextSnapshotVersion}";
    protected override Action? GetApplyEventAction(IAggregateEvent ev) =>
        ev.Payload switch
        {
            SnapshotManagerCreated created => () =>
            {
                Contents = new SnapshotManagerContents();
            },
            SnapshotManagerRequestAdded requestAdded => () =>
            {
                var requests = Contents.Requests.ToList();
                requests.Add(SnapshotKey(requestAdded.AggregateTypeName, requestAdded.TargetAggregateId, requestAdded.NextSnapshotVersion));
                Contents = Contents with { Requests = requests };
            },
            SnapshotManagerSnapshotTaken requestAdded => () =>
            {
                var requests = Contents.Requests.ToList();
                var requestTakens = Contents.RequestTakens.ToList();
                requests.Remove(SnapshotKey(requestAdded.AggregateTypeName, requestAdded.TargetAggregateId, requestAdded.NextSnapshotVersion));
                requestTakens.Add(SnapshotKey(requestAdded.AggregateTypeName, requestAdded.TargetAggregateId, requestAdded.NextSnapshotVersion));
                Contents = Contents with { Requests = requests, RequestTakens = requestTakens };
            },
            _ => null
        };
}
