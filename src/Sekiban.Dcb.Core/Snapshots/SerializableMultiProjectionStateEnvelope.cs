namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Envelope that contains either inline snapshot or offloaded snapshot reference.
/// </summary>
public sealed record SerializableMultiProjectionStateEnvelope(
    bool IsOffloaded,
    Sekiban.Dcb.MultiProjections.SerializableMultiProjectionState? InlineState,
    SerializableMultiProjectionStateOffloaded? OffloadedState)
{
}