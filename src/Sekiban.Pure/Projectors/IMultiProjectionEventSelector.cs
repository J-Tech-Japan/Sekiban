using Sekiban.Pure.Events;
namespace Sekiban.Pure.Projectors;

public interface IMultiProjectionEventSelector
{
    public List<string> GetRootPartitionKeys();
    public List<string> GetAggregateGroups();
    public bool GetEventSelector(IEvent e) =>
        (GetRootPartitionKeys().Count == 0 || GetRootPartitionKeys().Contains(e.PartitionKeys.RootPartitionKey)) &&
        (GetAggregateGroups().Count == 0 || GetAggregateGroups().Contains(e.PartitionKeys.Group));
}