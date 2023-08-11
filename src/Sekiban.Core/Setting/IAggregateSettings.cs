namespace Sekiban.Core.Setting;

/// <summary>
///     Aggregate setting interface.
/// </summary>
public interface IAggregateSettings
{
    /// <summary>
    ///     returns if it should take snapshot for the aggregate payload type.
    /// </summary>
    /// <param name="aggregatePayloadType"></param>
    /// <returns></returns>
    public bool ShouldTakeSnapshotForType(Type aggregatePayloadType);

    /// <summary>
    ///     Can aggregate payload use a hybrid type?
    ///     When using hybrid, the generated events are also stored in memory.
    ///     The only aggregates that can use this are those where it is certain that all events will occur in the same
    ///     instance.
    /// </summary>
    /// <param name="aggregatePayloadType"></param>
    /// <returns></returns>
    public bool CanUseHybrid(Type aggregatePayloadType);
    /// <summary>
    ///     returns snapshot frequency for the aggregate payload type.
    /// </summary>
    /// <param name="aggregatePayloadType"></param>
    /// <returns></returns>
    public int SnapshotFrequencyForType(Type aggregatePayloadType);
    /// <summary>
    ///     returns snapshot offset for the aggregate payload type.
    /// </summary>
    /// <param name="aggregatePayloadType"></param>
    /// <returns></returns>
    public int SnapshotOffsetForType(Type aggregatePayloadType);
    /// <summary>
    ///     returns if it should use update marker for the aggregate payload type.
    /// </summary>
    /// <param name="aggregatePayloadTypeName"></param>
    /// <returns></returns>
    public bool UseUpdateMarkerForType(string aggregatePayloadTypeName);
}
