using Sekiban.Core.Event;
namespace Sekiban.Core.Query.MultipleProjections;

public abstract class MultiProjectionBase<TProjectionPayload> : IMultiProjector<TProjectionPayload>
    where TProjectionPayload : IMultiProjectionPayload, new()
{
    private TProjectionPayload Payload { get; set; } = new();
    public Guid LastEventId { get; set; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int AppliedSnapshotVersion { get; set; }
    public int Version { get; set; }
    public void ApplyEvent(IAggregateEvent ev)
    {
        var action = GetApplyEventAction(ev, ev.GetPayload());
        if (action is null) { return; }
        action();
        Version++;
        LastEventId = ev.Id;
        LastSortableUniqueId = ev.SortableUniqueId;
    }
    public MultiProjectionState<TProjectionPayload> ToState() => new MultiProjectionState<TProjectionPayload>(
        Payload,
        LastEventId,
        LastSortableUniqueId,
        AppliedSnapshotVersion,
        Version);
    public void ApplySnapshot(MultiProjectionState<TProjectionPayload> snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        Payload = snapshot.Payload;
    }
    public virtual IList<string> TargetAggregateNames() => new List<string>();
    protected Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload)
    {
        var func = GetApplyEventFunc(ev, payload);
        return () =>
        {
            if (func == null) { return; }
            var result = func(Payload);
            Payload = result;
        };
    }
    protected abstract Func<TProjectionPayload, TProjectionPayload>? GetApplyEventFunc(IAggregateEvent ev, IEventPayload eventPayload);
}
