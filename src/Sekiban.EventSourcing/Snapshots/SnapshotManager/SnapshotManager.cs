using Sekiban.EventSourcing.Snapshots.SnapshotManager.Events;
namespace Sekiban.EventSourcing.Snapshots.SnapshotManager;

[AggregateContainerGroup(AggregateContainerGroup.InMemoryContainer)]
public class SnapshotManager : TransferableAggregateBase<SnapshotManagerDto>
{
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
    protected override Action? GetApplyEventAction(AggregateEvent ev) => ev switch
    {
        SnapshotManagerCreated created => () =>
        {
            CreatedAt = created.CreatedAt;
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
