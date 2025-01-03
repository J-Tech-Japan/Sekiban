using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.Projectors;

public interface IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev);
    public virtual string GetVersion() => "initial";
}
public interface IMultiProjector<TMultiAggregatePayload>
{
    public virtual string GetVersion() => "initial";
    public TMultiAggregatePayload Project(TMultiAggregatePayload payload, IEvent ev);
    public static abstract TMultiAggregatePayload GenerateInitialPayload();
}
public interface IMultiProjectionEventSelector
{
    public List<string> GetRootPartitionKeys();
    public List<string> GetAggregateGroups();
    public bool GetEventSelector(IEvent e) =>
        (GetRootPartitionKeys().Count == 0 || GetRootPartitionKeys().Contains(e.PartitionKeys.RootPartitionKey)) &&
        (GetAggregateGroups().Count == 0 || GetAggregateGroups().Contains(e.PartitionKeys.Group));
}
public record MultiProjectionEventSelector(List<string> RootPartitionKeys, List<string> AggregateGroups)
    : IMultiProjectionEventSelector
{
    public static MultiProjectionEventSelector All => new(new List<string>(), new List<string>());
    public List<string> GetRootPartitionKeys() => RootPartitionKeys;
    public List<string> GetAggregateGroups() => AggregateGroups;
}
