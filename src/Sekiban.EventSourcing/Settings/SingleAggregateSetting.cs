namespace Sekiban.EventSourcing.Settings;

public record SingleAggregateSetting(
    string AggregateClassName,
    bool MakeSnapshots = false,
    int? SnapshotByEachVersion = null,
    int? SnapshotOffset = null,
    IEnumerable<SingleAggregateProjectionSetting>? AggregateProjectionSettings = null);
public record SingleAggregateProjectionSetting(
    string AggregateClassName,
    bool MakeSnapshots = false,
    int? SnapshotByEachVersion = null,
    int? SnapshotOffset = null);
