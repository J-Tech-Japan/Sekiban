namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Lightweight projection head snapshot that avoids serializing projector payloads.
/// </summary>
[GenerateSerializer]
public sealed record MultiProjectionHeadStatusSnapshot(
    [property: Id(0)] string ProjectorName,
    [property: Id(1)] string? ProjectorVersion,
    [property: Id(2)] int CurrentEventVersion,
    [property: Id(3)] string? CurrentLastSortableUniqueId,
    [property: Id(4)] int ConsistentEventVersion,
    [property: Id(5)] string? ConsistentLastSortableUniqueId,
    [property: Id(6)] bool IsCatchUpInProgress,
    [property: Id(7)] string? CatchUpCurrentSortableUniqueId,
    [property: Id(8)] string? CatchUpTargetSortableUniqueId,
    [property: Id(9)] int PendingStreamEventCount);
