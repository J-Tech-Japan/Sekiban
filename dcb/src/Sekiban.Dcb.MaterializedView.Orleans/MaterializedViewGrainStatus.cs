namespace Sekiban.Dcb.MaterializedView.Orleans;

[GenerateSerializer]
public sealed record MaterializedViewGrainStatus(
    [property: Id(0)]
    string ServiceId,
    [property: Id(1)]
    string ViewName,
    [property: Id(2)]
    int ViewVersion,
    [property: Id(3)]
    bool Started,
    [property: Id(4)]
    bool CatchUpInProgress,
    [property: Id(5)]
    bool SubscriptionActive,
    [property: Id(6)]
    int BufferedEventCount,
    [property: Id(7)]
    string? CurrentPosition,
    [property: Id(8)]
    string? LastReceivedSortableUniqueId,
    [property: Id(9)]
    string? LastError,
    [property: Id(10)]
    DateTimeOffset? LastCatchUpStartedAt,
    [property: Id(11)]
    DateTimeOffset? LastCatchUpCompletedAt);
