namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Catch-up status for the multi-projection grain.
/// </summary>
[GenerateSerializer]
public record MultiProjectionCatchUpStatus(
    [property: Id(0)]
    bool IsActive,
    [property: Id(1)]
    string? CurrentPosition,
    [property: Id(2)]
    string? TargetPosition,
    [property: Id(3)]
    int BatchesProcessed,
    [property: Id(4)]
    int ConsecutiveEmptyBatches,
    [property: Id(5)]
    DateTime StartTime,
    [property: Id(6)]
    DateTime LastAttempt,
    [property: Id(7)]
    int PendingStreamEvents);
