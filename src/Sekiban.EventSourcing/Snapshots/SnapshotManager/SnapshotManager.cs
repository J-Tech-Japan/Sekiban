namespace Sekiban.EventSourcing.Snapshots.SnapshotManager;

[AggregateContainerGroup(AggregateContainerGroup.InMemoryContainer)]
public class SnapshotManager : TransferableAggregateBase<SnapshotManagerDto>
{
    private List<string> Requests { get; init; } = new();

    public SnapshotManager(Guid aggregateId) : base(aggregateId) { }
    protected override Action? GetApplyEventAction(AggregateEvent ev) =>
        throw new NotImplementedException();
    public override SnapshotManagerDto ToDto() =>
        throw new NotImplementedException();
    protected override void CopyPropertiesFromSnapshot(SnapshotManagerDto snapshot)
    {
        throw new NotImplementedException();
    }
}
