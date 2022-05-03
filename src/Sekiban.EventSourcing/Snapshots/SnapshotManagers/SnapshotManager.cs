using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Events;
namespace Sekiban.EventSourcing.Snapshots.SnapshotManagers;

[AggregateContainerGroup(AggregateContainerGroup.InMemoryContainer)]
public class SnapshotManager : TransferableAggregateBase<SnapshotManagerDto>
{
    private const int SnapshotCount = 30;
    public static Guid SharedId { get; } = new();
    private List<string> Requests
    {
        get;
    } = new();
    private DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public SnapshotManager(Guid aggregateId) : base(aggregateId) { }
    public SnapshotManager(Guid aggregateId, DateTime createdAt) : base(aggregateId)
    {
        AddAndApplyEvent(
            new SnapshotManagerCreated(
                aggregateId,
                createdAt));
    }
    public void ReportAggregateVersion(
        Guid snapshotManagerId,
        Type aggregateType,
        Guid targetAggregateId,
        int version,
        int? snapshotVersion)
    {
        var nextSnapshotVersion = version / SnapshotCount * SnapshotCount;
        var key = SnapshotKey(
            aggregateType.Name,
            targetAggregateId,
            nextSnapshotVersion,
            snapshotVersion);
        if (!Requests.Contains(key))
        {
            AddAndApplyEvent(
                new SnapshotManagerRequestAdded(
                    snapshotManagerId,
                    aggregateType.Name,
                    targetAggregateId,
                    nextSnapshotVersion,
                    snapshotVersion));
        }
    }
    private static string SnapshotKey(
        string aggregateTypeName,
        Guid targetAggregateId,
        int nextSnapshotVersion,
        int? snapshotVersion) =>
        $"{aggregateTypeName}_{targetAggregateId.ToString()}_{snapshotVersion ?? 0}_{nextSnapshotVersion}";
    protected override Action? GetApplyEventAction(AggregateEvent ev) => ev switch
    {
        SnapshotManagerCreated created => () =>
        {
            CreatedAt = created.CreatedAt;
        },
        SnapshotManagerRequestAdded requestAdded => () =>
        {
            Requests.Add(
                SnapshotKey(
                    requestAdded.AggregateTypeName,
                    requestAdded.TargetAggregateId,
                    requestAdded.NextSnapshotVersion,
                    requestAdded.SnapshotVersion));
        },
        _ => null
    };
    public override SnapshotManagerDto ToDto() => new(this)
    {
        Requests = Requests,
        CreatedAt = CreatedAt
    };
    protected override void CopyPropertiesFromSnapshot(SnapshotManagerDto snapshot)
    {
        Requests.AddRange(snapshot.Requests);
        CreatedAt = snapshot.CreatedAt;
    }
}
