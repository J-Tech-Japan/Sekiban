namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Lightweight persistent state for the multi-projection grain (v9).
///     Only stores key information for auxiliary/monitoring purposes.
///     Full state is stored in IMultiProjectionStateStore (Postgres/Cosmos).
/// </summary>
[GenerateSerializer]
public class MultiProjectionGrainState
{
    [Id(0)]
    public string ProjectorName { get; set; } = string.Empty;

    [Id(1)]
    public string? ProjectorVersion { get; set; }

    [Id(2)]
    public string? LastSortableUniqueId { get; set; }

    [Id(3)]
    public long EventsProcessed { get; set; }

    [Id(4)]
    public DateTime LastPersistTime { get; set; }

    // Legacy fields for migration (will be ignored after migration)
    [Id(5)]
    public string? SerializedState { get; set; }

    [Id(6)]
    public long StateSize { get; set; }

    [Id(7)]
    public string? SafeLastPosition { get; set; }

    [Id(8)]
    public string? LastPosition { get; set; }
}
