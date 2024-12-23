namespace Sekiban.Core.Snapshot.BackgroundServices;

/// <summary>
///     Background multi projections snapshot generate setting interface.
///     This interface is internal use for the sekiban.
/// </summary>
public interface IMultiProjectionsSnapshotGenerateSetting
{
    /// <summary>
    ///     Which multi projection class will take snapshot.
    /// </summary>
    /// <returns></returns>
    IEnumerable<Type> GetMultiProjectionsSnapshotTypes();
    /// <summary>
    ///     Which aggregate list class will take snapshot.
    /// </summary>
    /// <returns></returns>
    IEnumerable<Type> GetAggregateListSnapshotTypes();
    /// <summary>
    ///     Which single projection list class will take snapshot.
    /// </summary>
    /// <returns></returns>
    IEnumerable<Type> GetSingleProjectionListSnapshotTypes();

    /// <summary>
    ///     Snapshot generate interval seconds.
    /// </summary>
    /// <returns></returns>
    int GetExecuteIntervalSeconds();
    /// <summary>
    ///     minimal number of events to generate snapshot.
    /// </summary>
    /// <returns></returns>
    int GetMinimumNumberOfEventsToGenerateSnapshot();
    /// <summary>
    ///     Root partition keys fot this settings.
    /// </summary>
    /// <returns></returns>
    IEnumerable<string> GetRootPartitionKeys();
}
