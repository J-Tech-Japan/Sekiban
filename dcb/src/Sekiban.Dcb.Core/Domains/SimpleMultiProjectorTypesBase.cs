using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     Shared in-memory multi-projector registry behind the <c>SimpleMultiProjectorTypes</c> of Core, WithResult and
///     WithoutResult. It implements the exception-based <see cref="ICoreMultiProjectorTypes" />; each facade only adds
///     the registration methods for its own projector contract.
/// </summary>
public abstract class SimpleMultiProjectorTypesBase : ICoreMultiProjectorTypes, IStreamingMultiProjectorTypes
{
    private readonly ConcurrentDictionary<string, Func<IMultiProjectionPayload>> _initialPayloadGenerators = new();
    private readonly ConcurrentDictionary<string,
        Func<IMultiProjectionPayload, Event, List<ITag>, DcbDomainTypes, SortableUniqueId, IMultiProjectionPayload>> _projectorFunctions = new();
    private readonly ConcurrentDictionary<string, Type> _projectorTypes = new();
    private readonly ConcurrentDictionary<string, string> _projectorVersions = new();
    private readonly ConcurrentDictionary<string, (
        Func<DcbDomainTypes, string, object, SerializationResult> serialize,
        Func<DcbDomainTypes, string, ReadOnlySpan<byte>, object> deserialize)> _customSerializers = new();

    public IMultiProjectionPayload Project(
        string multiProjectorName,
        IMultiProjectionPayload payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        if (_projectorFunctions.TryGetValue(multiProjectorName, out var projectorFunc))
        {
            return projectorFunc(payload, ev, tags, domainTypes, safeWindowThreshold);
        }

        throw NotFound(multiProjectorName);
    }

    public string GetProjectorVersion(string multiProjectorName) =>
        _projectorVersions.TryGetValue(multiProjectorName, out var version)
            ? version
            : throw NotFound(multiProjectorName);

    public IReadOnlyList<string> GetAllProjectorNames() => _projectorTypes.Keys.ToList();

    public IMultiProjectionPayload GenerateInitialPayload(string multiProjectorName) =>
        _initialPayloadGenerators.TryGetValue(multiProjectorName, out var generator)
            ? generator()
            : throw NotFound(multiProjectorName);

    public IMultiProjectionPayload Deserialize(
        byte[] data,
        string multiProjectorName,
        JsonSerializerOptions jsonOptions)
    {
        // Get the projector type from the multiProjectorName
        if (!_projectorTypes.TryGetValue(multiProjectorName, out var projectorType))
        {
            throw NotFound(multiProjectorName);
        }

        // Since TProjector and TPayload are the same type now, use the projector type directly
        var json = Encoding.UTF8.GetString(data);
        var result = JsonSerializer.Deserialize(json, projectorType, jsonOptions);
        return result as IMultiProjectionPayload
            ?? throw new Exception("Deserialized object is not an IMultiProjectionPayload");
    }

    public Func<IMultiProjectionPayload> GetInitialPayloadGenerator(string multiProjectorName) =>
        _initialPayloadGenerators.TryGetValue(multiProjectorName, out var generator)
            ? generator
            : throw NotFound(multiProjectorName);

    public Type GetProjectorType(string multiProjectorName) =>
        _projectorTypes.TryGetValue(multiProjectorName, out var type)
            ? type
            : throw NotFound(multiProjectorName);

    /// <summary>
    ///     Serializes a multi-projection payload to bytes with size information.
    ///     Uses custom serialization if registered, otherwise falls back to JSON + Gzip.
    /// </summary>
    public SerializationResult Serialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        IMultiProjectionPayload payload)
    {
        if (string.IsNullOrWhiteSpace(safeWindowThreshold))
        {
            throw new ArgumentException("safeWindowThreshold must be supplied");
        }
        if (_customSerializers.TryGetValue(projectorName, out var serializers))
        {
            // Custom serializers return SerializationResult directly
            // They control their own compression
            return serializers.serialize(domainTypes, safeWindowThreshold, payload);
        }

        // Fallback: stream JSON directly into GZip to avoid holding json string + raw bytes + gzip bytes at once.
        var (compressed, originalSize) = GzipCompression.CompressJson(
            payload,
            payload.GetType(),
            domainTypes.JsonSerializerOptions);
        return new SerializationResult(compressed, originalSize, compressed.LongLength);
    }

    /// <summary>
    ///     Stream-oriented fallback. For the default JSON+Gzip serializer, the compressed
    ///     bytes are piped straight into the destination stream, so a large multi-projection
    ///     snapshot never needs the full compressed payload in managed memory. Custom
    ///     serializers still fall back to materialized bytes — copied through the destination
    ///     stream — because they typically emit compact binary forms already.
    /// </summary>
    public SerializationSizeInfo SerializeToStream(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        IMultiProjectionPayload payload,
        Stream destination)
    {
        MultiProjectorStreamSerializationHelper.ValidateCommonArguments(safeWindowThreshold, destination);

        if (_customSerializers.TryGetValue(projectorName, out var serializers))
        {
            var customResult = serializers.serialize(domainTypes, safeWindowThreshold, payload);
            return MultiProjectorStreamSerializationHelper.CopyCustomResultToStream(destination, customResult);
        }

        return MultiProjectorStreamSerializationHelper.WriteGzipJsonToStream(
            destination,
            payload,
            domainTypes.JsonSerializerOptions);
    }

    /// <summary>
    ///     Deserializes a multi-projection payload.
    ///     Uses custom deserialization if registered, otherwise falls back to standard JSON deserialization.
    /// </summary>
    public IMultiProjectionPayload Deserialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        byte[] data)
    {
        if (string.IsNullOrWhiteSpace(safeWindowThreshold))
        {
            throw new ArgumentException("safeWindowThreshold must be supplied");
        }
        if (_customSerializers.TryGetValue(projectorName, out var serializers))
        {
            return (IMultiProjectionPayload)serializers.deserialize(domainTypes, safeWindowThreshold, data);
        }

        // Fallback: check if data is gzip-compressed (magic bytes 0x1f 0x8b) for backward compatibility
        var jsonBytes = data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b
            ? GzipCompression.Decompress(data) // Legacy format: gzip-compressed JSON
            : data; // New format: raw UTF-8 JSON
        var json = Encoding.UTF8.GetString(jsonBytes);
        if (_projectorTypes.TryGetValue(projectorName, out var type))
        {
            return JsonSerializer.Deserialize(json, type, domainTypes.JsonSerializerOptions) as IMultiProjectionPayload
                ?? throw new InvalidOperationException($"Failed to deserialize to {type.Name}");
        }

        throw new KeyNotFoundException($"Projector '{projectorName}' not found");
    }

    /// <inheritdoc />
    bool ICoreMultiProjectorTypes.RegisterProjectorWithCustomSerialization<T>() =>
        RegisterCoreProjectorWithCustomSerialization<T>();

    public bool SupportsStreamDeserialization(string projectorName) =>
        StreamingMultiProjectorTypesSupport.Supports(this, projectorName);

    public Task<IMultiProjectionPayload> DeserializeFromStreamAsync(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        Stream source,
        CancellationToken cancellationToken = default) =>
        StreamingMultiProjectorTypesSupport.DeserializeAsync(
            this,
            projectorName,
            domainTypes,
            safeWindowThreshold,
            source,
            cancellationToken);

    /// <summary>
    ///     Registers a projector under its name. Registering the same name again with the same type is a no-op for the
    ///     function; a different type is rejected.
    /// </summary>
    protected void RegisterProjectorEntry<TProjector>(
        string projectorName,
        string projectorVersion,
        Func<IMultiProjectionPayload> generateInitialPayload,
        Func<IMultiProjectionPayload, Event, List<ITag>, DcbDomainTypes, SortableUniqueId, IMultiProjectionPayload> project)
    {
        if (!_projectorFunctions.TryAdd(projectorName, project))
        {
            // Check if it's the same type being registered again
            if (_projectorTypes.TryGetValue(projectorName, out var existingType) && existingType != typeof(TProjector))
            {
                var existingTypeName = existingType.FullName ?? existingType.Name;
                var newTypeName = typeof(TProjector).FullName ?? typeof(TProjector).Name;
                throw new InvalidOperationException(
                    $"Multi projector name '{projectorName}' is already registered with type '{existingTypeName}', cannot register with type '{newTypeName}'.");
            }
        }
        else
        {
            // Only register if function was successfully added
            _projectorTypes[projectorName] = typeof(TProjector);
        }

        _projectorVersions[projectorName] = projectorVersion;
        _initialPayloadGenerators[projectorName] = generateInitialPayload;
        StreamingMultiProjectorTypesSupport.RegisterReflection(this, projectorName, typeof(TProjector));
    }

    /// <summary>
    ///     Registers a projector that implements the exception-based core projector contract.
    /// </summary>
    protected void RegisterCoreProjector<TProjector>() where TProjector : ICoreMultiProjector<TProjector>, new() =>
        RegisterProjectorEntry<TProjector>(
            TProjector.MultiProjectorName,
            TProjector.MultiProjectorVersion,
            () => TProjector.GenerateInitialPayload(),
            ProjectAs<TProjector>(TProjector.Project));

    /// <summary>
    ///     Registers a projector with custom serialization delegates. The entries are written directly (a second
    ///     registration under the same name keeps the first projector function).
    /// </summary>
    protected bool RegisterCustomSerializedProjector<TProjector>(
        string projectorName,
        string projectorVersion,
        Func<IMultiProjectionPayload> generateInitialPayload,
        Func<IMultiProjectionPayload, Event, List<ITag>, DcbDomainTypes, SortableUniqueId, IMultiProjectionPayload> project,
        Func<DcbDomainTypes, string, object, SerializationResult> serialize,
        Func<DcbDomainTypes, string, ReadOnlySpan<byte>, object> deserialize)
        where TProjector : new()
    {
        _customSerializers[projectorName] = (serialize, deserialize);
        _projectorFunctions.TryAdd(projectorName, project);
        _projectorTypes[projectorName] = typeof(TProjector);
        _projectorVersions[projectorName] = projectorVersion;
        _initialPayloadGenerators[projectorName] = generateInitialPayload;
        StreamingMultiProjectorTypesSupport.RegisterCustomOrRemove<TProjector>(this, projectorName);
        return true;
    }

    /// <summary>
    ///     Registers a projector that implements the exception-based core custom-serialization contract.
    /// </summary>
    protected virtual bool RegisterCoreProjectorWithCustomSerialization<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new() =>
        RegisterCustomSerializedProjector<T>(
            T.MultiProjectorName,
            T.MultiProjectorVersion,
            () => T.GenerateInitialPayload(),
            ProjectAs<T>(T.Project),
            (domain, safeWindowThreshold, payload) => T.Serialize(domain, safeWindowThreshold, (T)payload),
            (domain, safeWindowThreshold, data) => T.Deserialize(domain, safeWindowThreshold, data));

    /// <summary>
    ///     Adapts a typed projector function to the registry's untyped one.
    /// </summary>
    protected static Func<IMultiProjectionPayload, Event, List<ITag>, DcbDomainTypes, SortableUniqueId, IMultiProjectionPayload>
        ProjectAs<TProjector>(Func<TProjector, Event, List<ITag>, DcbDomainTypes, SortableUniqueId, TProjector> project)
        where TProjector : IMultiProjectionPayload =>
        (payload, ev, tags, domainTypes, safeWindowThreshold) =>
            payload is TProjector typedPayload
                ? project(typedPayload, ev, tags, domainTypes, safeWindowThreshold)
                : throw new InvalidCastException($"Payload is not of type {typeof(TProjector).Name}");

    private static Exception NotFound(string multiProjectorName) =>
        new($"Multi projector '{multiProjectorName}' not found");
}
