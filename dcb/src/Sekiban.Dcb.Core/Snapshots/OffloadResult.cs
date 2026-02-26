namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Result of the stream offload decision: either inlined data or offloaded metadata.
/// </summary>
public readonly record struct OffloadResult(
    bool IsOffloaded,
    byte[]? InlineData,
    string? OffloadKey,
    string? OffloadProvider);
