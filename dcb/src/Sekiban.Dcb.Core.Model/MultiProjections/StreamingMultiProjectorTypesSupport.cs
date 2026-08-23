using System.Collections.Concurrent;
using System.Text.Json.Serialization.Metadata;
using ResultBoxes;
using Sekiban.Dcb.Domains;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Shared implementation behind the optional stream-restore registry capability. It is internal so the public
///     capability remains limited to <see cref="IStreamingMultiProjectorTypes" /> and registries compiled against
///     older packages still use the actor's buffered compatibility route.
/// </summary>
internal sealed class StreamingMultiProjectorTypesSupport
{
    private readonly ConcurrentDictionary<string,
        Func<DcbDomainTypes, string, Stream, CancellationToken, Task<IMultiProjectionPayload>>> _deserializers = new();

    internal bool Supports(string projectorName) => _deserializers.ContainsKey(projectorName);

    internal void RegisterReflection(string projectorName, Type projectorType) =>
        _deserializers[projectorName] = async (domainTypes, _, source, cancellationToken) =>
        {
            var deserialized = await StreamSnapshotPayloadDeserializer.DeserializeJsonAsync(
                    source,
                    projectorType,
                    domainTypes.JsonSerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return deserialized as IMultiProjectionPayload
                ?? throw new InvalidOperationException(
                    $"Failed to deserialize streaming payload to {projectorType.Name}.");
        };

    internal void RegisterJsonTypeInfo<TProjector>(
        string projectorName,
        JsonTypeInfo<TProjector> typeInfo) =>
        _deserializers[projectorName] = async (_, _, source, cancellationToken) =>
        {
            var deserialized = await StreamSnapshotPayloadDeserializer.DeserializeJsonAsync(
                    source,
                    typeInfo,
                    cancellationToken)
                .ConfigureAwait(false);
            return deserialized as IMultiProjectionPayload
                ?? throw new InvalidOperationException(
                    $"Failed to deserialize streaming payload to {typeof(TProjector).Name}.");
        };

    internal void RegisterCustomOrRemove<TProjector>(string projectorName)
        where TProjector : new()
    {
        if (typeof(ICoreMultiProjectorWithStreamDeserialization).IsAssignableFrom(typeof(TProjector)))
        {
            _deserializers[projectorName] = (domainTypes, safeWindowThreshold, source, cancellationToken) =>
                ((ICoreMultiProjectorWithStreamDeserialization)(object)new TProjector())
                .DeserializeFromStreamAsync(domainTypes, safeWindowThreshold, source, cancellationToken);
            return;
        }

        // A custom serializer's bytes are not necessarily JSON+gzip, so it must explicitly opt in rather than
        // inheriting the reflection projector path registered before the custom serializer was installed.
        _deserializers.TryRemove(projectorName, out _);
    }

    internal async Task<ResultBox<IMultiProjectionPayload>> DeserializeAsync(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        Stream source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(safeWindowThreshold))
        {
            return ResultBox.Error<IMultiProjectionPayload>(
                new ArgumentException("safeWindowThreshold must be supplied"));
        }

        if (source is null)
        {
            return ResultBox.Error<IMultiProjectionPayload>(new ArgumentNullException(nameof(source)));
        }

        if (!_deserializers.TryGetValue(projectorName, out var deserialize))
        {
            return ResultBox.Error<IMultiProjectionPayload>(
                new NotSupportedException($"Projector '{projectorName}' does not support streaming deserialization."));
        }

        try
        {
            return ResultBox.FromValue(
                await deserialize(domainTypes, safeWindowThreshold, source, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IMultiProjectionPayload>(ex);
        }
    }
}
