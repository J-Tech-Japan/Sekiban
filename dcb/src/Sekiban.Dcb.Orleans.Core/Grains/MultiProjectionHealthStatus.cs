namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Health status of a MultiProjection grain.
///     Used for monitoring and diagnostics.
/// </summary>
[GenerateSerializer]
public record MultiProjectionHealthStatus(
    /// <summary>Whether the grain has completed initialization.</summary>
    [property: Id(0)] bool IsInitialized,

    /// <summary>Whether the projection actor has been created.</summary>
    [property: Id(1)] bool HasProjectionActor,

    /// <summary>Number of events processed by this grain.</summary>
    [property: Id(2)] long EventsProcessed,

    /// <summary>Last error message, if any.</summary>
    [property: Id(3)] string? LastError,

    /// <summary>Whether catch-up from event store is currently active.</summary>
    [property: Id(4)] bool IsCatchUpActive,

    /// <summary>Last time state was persisted.</summary>
    [property: Id(5)] DateTime? LastPersistTime,

    /// <summary>Last processed event position.</summary>
    [property: Id(6)] string? LastSortableUniqueId,

    /// <summary>Number of events pending in stream queue.</summary>
    [property: Id(7)] int PendingStreamEvents,

    /// <summary>When state was restored during activation.</summary>
    [property: Id(8)] DateTime? StateRestoredAt,

    /// <summary>How state was restored during activation.</summary>
    [property: Id(9)] StateRestoreSource StateRestoreSource,

    /// <summary>Whether the grain is in a healthy state for queries.</summary>
    [property: Id(10)] bool IsHealthy
);
