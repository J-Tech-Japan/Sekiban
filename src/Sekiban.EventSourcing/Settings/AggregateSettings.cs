namespace Sekiban.EventSourcing.Settings;

public class AggregateSettings : IAggregateSettings
{
    public AggregateSettingHelper Helper { get; init; } = new();

    public bool ShouldTakeSnapshotForType(Type originalType) =>
        Helper.Exceptions.FirstOrDefault(m => m.AggregateClassName == originalType.Name)?.MakeSnapshots ?? Helper.TakeSnapshotDefault;

    public bool CanUseHybrid(Type originalType) =>
        Helper.Exceptions.FirstOrDefault(m => m.AggregateClassName == originalType.Name)?.UseHybrid ?? Helper.UseHybridDefault;
    public int SnapshotFrequencyForType(Type originalType) =>
        Helper.Exceptions.FirstOrDefault(m => m.AggregateClassName == originalType.Name)?.SnapshotFrequency ?? Helper.SnapshotFrequencyDefault;
    public int SnapshotOffsetForType(Type originalType) =>
        Helper.Exceptions.FirstOrDefault(m => m.AggregateClassName == originalType.Name)?.SnapshotOffset ?? Helper.SnapshotOffsetDefault;
}
