namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Status information for the multi-projection grain
/// </summary>
[GenerateSerializer]
public record MultiProjectionGrainStatus(
    [property: Id(0)]
    string ProjectorName,
    [property: Id(1)]
    bool IsSubscriptionActive,
    [property: Id(2)]
    bool IsCaughtUp,
    [property: Id(3)]
    string? CurrentPosition,
    [property: Id(4)]
    long EventsProcessed,
    [property: Id(5)]
    DateTime? LastEventTime,
    [property: Id(6)]
    DateTime? LastPersistTime,
    [property: Id(7)]
    long StateSize,
    [property: Id(8)]
    bool HasError,
    [property: Id(9)]
    string? LastError);
