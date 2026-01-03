namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Options for building multi projection safe state.
/// </summary>
public sealed record MultiProjectionBuildOptions
{
    public int MinEventThreshold { get; init; } = 3000;
    public int SafeWindowMs { get; init; } = 20000;
    public int OffloadThresholdBytes { get; init; } = 1_000_000;
    public bool Force { get; init; }
    public bool DryRun { get; init; }
    public string? SpecificProjector { get; init; }
}
