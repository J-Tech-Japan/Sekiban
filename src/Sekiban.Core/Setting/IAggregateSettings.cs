namespace Sekiban.Core.Setting;

public interface IAggregateSettings
{
    public bool ShouldTakeSnapshotForType(Type aggregatePayloadType);

    /// <summary>
    ///     Can it use a hybrid type?
    ///     When using hybrid, the generated events are also stored in memory.
    ///     The only aggregates that can use this are those where it is certain that all events will occur in the same
    ///     instance.
    /// </summary>
    /// <param name="aggregatePayloadType"></param>
    /// <returns></returns>
    public bool CanUseHybrid(Type aggregatePayloadType);

    public int SnapshotFrequencyForType(Type aggregatePayloadType);
    public int SnapshotOffsetForType(Type aggregatePayloadType);
    public bool UseUpdateMarkerForType(string aggregatePayloadTypeName);
}
