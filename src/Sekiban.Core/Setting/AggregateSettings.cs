namespace Sekiban.Core.Setting;

public class AggregateSettings : IAggregateSettings
{
    public AggregateSettingHelper Helper { get; init; } = new();

    public bool ShouldTakeSnapshotForType(Type originalType)
    {
        return Helper.Exceptions.FirstOrDefault(m => m.AggregateClassName == originalType.Name)?.MakeSnapshots ?? Helper.TakeSnapshotDefault;
    }

    public bool CanUseHybrid(Type originalType)
    {
        return Helper.Exceptions.FirstOrDefault(m => m.AggregateClassName == originalType.Name)?.UseHybrid ?? Helper.UseHybridDefault;
    }
    public int SnapshotFrequencyForType(Type originalType)
    {
        return Helper.Exceptions.FirstOrDefault(m => m.AggregateClassName == originalType.Name)?.SnapshotFrequency ?? Helper.SnapshotFrequencyDefault;
    }
    public int SnapshotOffsetForType(Type originalType)
    {
        return Helper.Exceptions.FirstOrDefault(m => m.AggregateClassName == originalType.Name)?.SnapshotOffset ?? Helper.SnapshotOffsetDefault;
    }
    public bool UseUpdateMarkerForType(string originalType)
    {
        return Helper.Exceptions.FirstOrDefault(m => m.AggregateClassName == originalType)?.UseUpdateMarker ?? Helper.UseUpdateMarker;
    }
}