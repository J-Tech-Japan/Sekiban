namespace Sekiban.Dcb.Storage.Checkpoints;

/// <summary>
///     Resolves the checkpoint capability from a LIVE state-store instance (never a type name), mirroring the G15/G16
///     <c>SekibanDcbCapabilityResolver</c> discipline. Not implementing <see cref="IGenerationAwareCheckpointStore" />
///     means the capability is unsupported — silence is never read as a capability.
/// </summary>
public static class CheckpointCapabilityResolver
{
    public static CheckpointStoreCapabilityDescriptor Describe(object? store, string role) => store switch
    {
        ICheckpointStoreCapabilityProvider provider => provider.DescribeCheckpointCapability(),
        _ => CheckpointStoreCapabilityDescriptor.None(role)
    };

    /// <summary>
    ///     True only when the live instance both implements the optional surface AND advertises the generation/tombstone
    ///     CAS kind. Used by the product path to decide CAS-adoption vs the fail-closed G14 fallback.
    /// </summary>
    public static bool SupportsGenerationCas(object? store) =>
        store is IGenerationAwareCheckpointStore &&
        Describe(store, "checkpoint store").Supports(CheckpointCapabilityKind.GenerationTombstoneCas);
}
