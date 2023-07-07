namespace Sekiban.Core.Snapshot.BackgroundServices;

public interface IMultiProjectionsSnapshotGenerateSetting
{
    IEnumerable<Type> GetMultiProjectionsSnapshotTypes();
    IEnumerable<Type> GetAggregateListSnapshotTypes();
    IEnumerable<Type> GetSingleProjectionListSnapshotTypes();

    int GetExecuteIntervalSeconds();

    int GetMinimumNumberOfEventsToGenerateSnapshot();

    IEnumerable<string> GetRootPartitionKeys();
}
