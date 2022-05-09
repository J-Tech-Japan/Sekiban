namespace Sekiban.EventSourcing.Settings;

public record SingleAggregateSetting(
    string AggregateClassName,
    bool? UseHybrid = false,
    bool? MakeSnapshots = false,
    int? SnapshotByEachVersion = null,
    int? SnapshotOffset = null);
public record SingleAggregateProjectionSetting(
    string ProjectionClassName,
    bool? UseHybrid = false,
    bool? MakeSnapshots = false,
    int? SnapshotByEachVersion = null,
    int? SnapshotOffset = null);
