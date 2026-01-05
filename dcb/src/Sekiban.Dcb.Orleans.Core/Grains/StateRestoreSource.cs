namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Indicates how the MultiProjection state was restored during activation.
/// </summary>
[GenerateSerializer]
public enum StateRestoreSource
{
    /// <summary>State not yet restored.</summary>
    None = 0,

    /// <summary>Restored from external store (Cosmos/Postgres).</summary>
    ExternalStore = 1,

    /// <summary>Rebuilt via full catch-up from event store.</summary>
    FullCatchUp = 2,

    /// <summary>Restoration failed, Grain is in unhealthy state.</summary>
    Failed = 3
}
