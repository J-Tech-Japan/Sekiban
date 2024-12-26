namespace Sekiban.Core.Setting;

/// <summary>
///     Aggregate Setting Implementation.
/// </summary>
public class AggregateSettings : IAggregateSettings
{
    public AggregateSettingHelper Helper { get; init; } = new();

    public bool ShouldTakeSnapshotForType(Type aggregatePayloadType)
    {
        return Helper.Exceptions.FirstOrDefault(m => m.AggregateClassName == aggregatePayloadType.Name)
                ?.MakeSnapshots ??
            Helper.TakeSnapshotDefault;
    }

    public int SnapshotFrequencyForType(Type aggregatePayloadType)
    {
        return Helper.Exceptions.FirstOrDefault(m => m.AggregateClassName == aggregatePayloadType.Name)
                ?.SnapshotFrequency ??
            Helper.SnapshotFrequencyDefault;
    }

    public int SnapshotOffsetForType(Type aggregatePayloadType)
    {
        return Helper.Exceptions.FirstOrDefault(m => m.AggregateClassName == aggregatePayloadType.Name)
                ?.SnapshotOffset ??
            Helper.SnapshotOffsetDefault;
    }

    public bool UseUpdateMarkerForType(string aggregatePayloadTypeName)
    {
        return Helper.Exceptions.FirstOrDefault(m => m.AggregateClassName == aggregatePayloadTypeName)
                ?.UseUpdateMarker ??
            Helper.UseUpdateMarker;
    }
}
