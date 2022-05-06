using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Events;
namespace Sekiban.EventSourcing.Snapshots.SnapshotManagers;

[AggregateContainerGroup(AggregateContainerGroup.InMemoryContainer)]
public class SnapshotManager : TransferableAggregateBase<SnapshotManagerDto>
{
    private const int SnapshotCount = 40;
    private const int SnapshotTakeOffset = 15;
    public static Guid SharedId { get; } = new();
    private List<string> Requests
    {
        get;
    } = new();
    private List<string> RequestTakens
    {
        get;
    } = new();
    private DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public SnapshotManager(Guid aggregateId) : base(aggregateId) { }
    public SnapshotManager(Guid aggregateId, DateTime createdAt) : base(aggregateId)
    {
        AddAndApplyEvent(new SnapshotManagerCreated(aggregateId, createdAt));
    }
    public void ReportAggregateVersion(Guid snapshotManagerId, Type aggregateType, Guid targetAggregateId, int version, int? snapshotVersion)
    {
        var nextSnapshotVersion = version / SnapshotCount * SnapshotCount;
        var offset = version - nextSnapshotVersion;
        if (nextSnapshotVersion == 0) { return; }
        var key = SnapshotKey(aggregateType.Name, targetAggregateId, nextSnapshotVersion);
        if (!Requests.Contains(key) && !RequestTakens.Contains(key))
        {
            AddAndApplyEvent(
                new SnapshotManagerRequestAdded(snapshotManagerId, aggregateType.Name, targetAggregateId, nextSnapshotVersion, snapshotVersion));
        }
        if (Requests.Contains(key) && !RequestTakens.Contains(key) && offset > SnapshotTakeOffset)
        {
            AddAndApplyEvent(
                new SnapshotManagerSnapshotTaken(snapshotManagerId, aggregateType.Name, targetAggregateId, nextSnapshotVersion, snapshotVersion));
        }
    }
    private static string SnapshotKey(string aggregateTypeName, Guid targetAggregateId, int nextSnapshotVersion) =>
        $"{aggregateTypeName}_{targetAggregateId.ToString()}_{nextSnapshotVersion}";
    protected override Action? GetApplyEventAction(AggregateEvent ev) => ev switch
    {
        SnapshotManagerCreated created => () =>
        {
            CreatedAt = created.CreatedAt;
        },
        SnapshotManagerRequestAdded requestAdded => () =>
        {
            Requests.Add(SnapshotKey(requestAdded.AggregateTypeName, requestAdded.TargetAggregateId, requestAdded.NextSnapshotVersion));
        },
        SnapshotManagerSnapshotTaken requestAdded => () =>
        {
            Requests.Remove(SnapshotKey(requestAdded.AggregateTypeName, requestAdded.TargetAggregateId, requestAdded.NextSnapshotVersion));
            RequestTakens.Add(SnapshotKey(requestAdded.AggregateTypeName, requestAdded.TargetAggregateId, requestAdded.NextSnapshotVersion));
        },
        _ => null
    };
    public override SnapshotManagerDto ToDto() => new(this)
    {
        Requests = Requests,
        RequestTakens = RequestTakens,
        CreatedAt = CreatedAt
    };
    protected override void CopyPropertiesFromSnapshot(SnapshotManagerDto snapshot)
    {
        Requests.AddRange(snapshot.Requests);
        RequestTakens.AddRange(snapshot.RequestTakens);
        CreatedAt = snapshot.CreatedAt;
    }
}
