using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Capabilities;

/// <summary>
///     Declares whether a store can perform a tagged callback stream without first routing through its list API.
///     Silence is deliberately fail-closed: a caller must not infer native streaming from interface membership alone.
/// </summary>
public sealed record TaggedStreamCapabilityDescriptor(bool NativeStreaming, string ProviderName)
{
    public static TaggedStreamCapabilityDescriptor None(string providerName) => new(false, providerName);

    public static TaggedStreamCapabilityDescriptor Native(string providerName) => new(true, providerName);
}

/// <summary>
///     Implemented by a live store instance to state whether its tagged-stream member has native streaming semantics.
/// </summary>
public interface ITaggedStreamCapabilityProvider
{
    TaggedStreamCapabilityDescriptor DescribeTaggedStream();
}

/// <summary>
///     The fail-closed result of resolving the optional tagged-stream capability on a live store instance.
/// </summary>
public sealed record TaggedStreamCapabilityResolution(
    bool IsSupported,
    IStreamingTaggedSerializableEventStore? StreamStore,
    TaggedStreamCapabilityDescriptor Descriptor,
    string? UnsupportedReason);
