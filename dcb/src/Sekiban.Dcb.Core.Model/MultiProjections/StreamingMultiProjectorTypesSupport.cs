using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using ResultBoxes;
using Sekiban.Dcb.Domains;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Shared implementation behind the optional stream-restore registry capability. It is internal so the public
///     capability remains limited to <see cref="IStreamingMultiProjectorTypes" /> and registries compiled against
///     older packages still use the actor's buffered compatibility route.
/// </summary>
internal static class StreamingMultiProjectorTypesSupport
{
    // Keep the state private to each registry without making every registry repeat the capability forwarding code.
    // ConditionalWeakTable does not extend a registry's lifetime.
    private static readonly ConditionalWeakTable<object, RegistryDeserializers> DeserializersByRegistry = new();

    internal static bool Supports(object registry, string projectorName) =>
        GetDeserializers(registry).Supports(projectorName);

    internal static void RegisterReflection(object registry, string projectorName, Type projectorType) =>
        GetDeserializers(registry).RegisterReflection(projectorName, projectorType);

    internal static void RegisterJsonTypeInfo<TProjector>(
        object registry,
        string projectorName,
        JsonTypeInfo<TProjector> typeInfo) => GetDeserializers(registry).RegisterJsonTypeInfo(projectorName, typeInfo);

    internal static void RegisterCustomOrRemove<TProjector>(object registry, string projectorName)
        where TProjector : new() => GetDeserializers(registry).RegisterCustomOrRemove<TProjector>(projectorName);

    internal static Task<ResultBox<IMultiProjectionPayload>> DeserializeAsync(
        object registry,
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        Stream source,
        CancellationToken cancellationToken) => GetDeserializers(registry).DeserializeAsync(
        projectorName,
        domainTypes,
        safeWindowThreshold,
        source,
        cancellationToken,
        static name => new NotSupportedException($"Projector '{name}' does not support streaming deserialization."));

    internal static Task<ResultBox<IMultiProjectionPayload>> DeserializeWithProjectorNotFoundAsync(
        object registry,
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        Stream source,
        CancellationToken cancellationToken) => GetDeserializers(registry).DeserializeAsync(
        projectorName,
        domainTypes,
        safeWindowThreshold,
        source,
        cancellationToken,
        static name => new Exception($"Projector not found: {name}"));

    private static RegistryDeserializers GetDeserializers(object registry) =>
        DeserializersByRegistry.GetValue(registry, static _ => new RegistryDeserializers());

    private sealed class RegistryDeserializers
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

        internal void RegisterJsonTypeInfo<TProjector>(string projectorName, JsonTypeInfo<TProjector> typeInfo) =>
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
            CancellationToken cancellationToken,
            Func<string, Exception> unsupportedProjectorException)
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
                return ResultBox.Error<IMultiProjectionPayload>(unsupportedProjectorException(projectorName));
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
}
