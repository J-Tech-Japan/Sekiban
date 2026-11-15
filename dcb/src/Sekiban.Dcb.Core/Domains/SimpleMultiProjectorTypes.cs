using Sekiban.Dcb.Events;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     Simple in-memory registry for multi projectors.
/// </summary>
internal class SimpleMultiProjectorTypes : ICoreMultiProjectorTypes, IStreamingMultiProjectorTypes
{
    private readonly ConcurrentDictionary<string, Func<IMultiProjectionPayload>> _initialPayloadGenerators = new();
    private readonly ConcurrentDictionary<string,
        Func<IMultiProjectionPayload, Event, List<ITag>, DcbDomainTypes, SortableUniqueId, IMultiProjectionPayload>> _projectorFunctions = new();
    private readonly ConcurrentDictionary<string, Type> _projectorTypes = new();
    private readonly ConcurrentDictionary<string, string> _projectorVersions = new();
    private readonly ConcurrentDictionary<Type, string> _typeToNameMap = new();
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

    public IReadOnlyList<string> GetAllProjectorNames()
    {
        return _projectorTypes.Keys.ToList();
    }


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
    ///     Register a multi projector type using its static GetMultiProjectorName
    /// </summary>
    public void RegisterProjector<TProjector>() where TProjector : ICoreMultiProjector<TProjector>, new()
    {
        var projectorName = TProjector.MultiProjectorName;

        // Register the projector function
        Func<IMultiProjectionPayload, Event, List<ITag>, DcbDomainTypes, SortableUniqueId, IMultiProjectionPayload> projectFunc
            = (payload, ev, tags, domainTypes, safeWindowThreshold) =>
                payload is TProjector typedPayload
                    ? TProjector.Project(typedPayload, ev, tags, domainTypes, safeWindowThreshold)
                    : throw new InvalidCastException($"Payload is not of type {typeof(TProjector).Name}");

        if (!_projectorFunctions.TryAdd(projectorName, projectFunc))
        {
            // Check if it's the same type being registered again
            if (_projectorTypes.TryGetValue(projectorName, out var existingType))
            {
                if (existingType != typeof(TProjector))
                {
                    var existingTypeName = existingType.FullName ?? existingType.Name;
                    var newTypeName = typeof(TProjector).FullName ?? typeof(TProjector).Name;
                    throw new InvalidOperationException(
                        $"Multi projector name '{projectorName}' is already registered with type '{existingTypeName}', cannot register with type '{newTypeName}'.");
                }
            }
        }
        else
        {
            // Only register if function was successfully added
            _projectorTypes[projectorName] = typeof(TProjector);
            _typeToNameMap[typeof(TProjector)] = projectorName;
        }

        // Register the version
        _projectorVersions[projectorName] = TProjector.MultiProjectorVersion;

        // Register the initial payload generator
        _initialPayloadGenerators[projectorName] = () => TProjector.GenerateInitialPayload();
        StreamingMultiProjectorTypesSupport.RegisterReflection(this, projectorName, typeof(TProjector));
    }

    /// <summary>
    ///     Register a multi projector type using its static GetMultiProjectorName.
    ///     The core projector contract is exception based, so this is the same as <see cref="RegisterProjector{TProjector}" />.
    /// </summary>
    public void RegisterProjectorWithoutResult<TProjector>() where TProjector : ICoreMultiProjector<TProjector>, new() =>
        RegisterProjector<TProjector>();

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
        var compressedSize = compressed.LongLength;

        return new SerializationResult(
            compressed,
            originalSize,
            compressedSize);
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
    ///     Deserializes a JSON string to a multi-projection payload.
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
        byte[] jsonBytes;
        if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
        {
            // Legacy format: gzip-compressed JSON
            jsonBytes = GzipCompression.Decompress(data);
        }
        else
        {
            // New format: raw UTF-8 JSON
            jsonBytes = data;
        }
        var json = Encoding.UTF8.GetString(jsonBytes);
        if (_projectorTypes.TryGetValue(projectorName, out var type))
        {
            return JsonSerializer.Deserialize(json, type, domainTypes.JsonSerializerOptions) as IMultiProjectionPayload
                ?? throw new InvalidOperationException($"Failed to deserialize to {type.Name}");
        }

        throw new KeyNotFoundException($"Projector '{projectorName}' not found");
    }

    /// <summary>
    ///     Registers a projector with custom serialization support.
    ///     The projector must implement IMultiProjectorWithCustomSerialization interface.
    /// </summary>
    public bool RegisterProjectorWithCustomSerialization<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new()
    {
        var projectorName = T.MultiProjectorName;

        // Store custom serialization delegates
        _customSerializers[projectorName] = (
            serialize: (domain, safeWindowThreshold, payload) => T.Serialize(domain, safeWindowThreshold, (T)payload),
            deserialize: (domain, safeWindowThreshold, data) => T.Deserialize(domain, safeWindowThreshold, data)
        );

        // Register the projector using the base RegisterProjector method
        // Since ICoreMultiProjectorWithCustomSerialization<T> inherits from ICoreMultiProjector<T>,
        // we can directly call RegisterProjector
        RegisterProjector<T>();
        StreamingMultiProjectorTypesSupport.RegisterCustomOrRemove<T>(this, projectorName);

        return true;
    }
    /// <summary>
    ///     Registers a projector with custom serialization support.
    ///     The core projector contract is exception based, so this is the same as
    ///     <see cref="RegisterProjectorWithCustomSerialization{T}" />.
    /// </summary>
    public bool RegisterProjectorWithCustomSerializationWithoutResult<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new() =>
        RegisterProjectorWithCustomSerialization<T>();

    public bool SupportsStreamDeserialization(string projectorName) => StreamingMultiProjectorTypesSupport.Supports(this, projectorName);

    public Task<IMultiProjectionPayload> DeserializeFromStreamAsync(string projectorName, DcbDomainTypes domainTypes, string safeWindowThreshold, Stream source, CancellationToken cancellationToken = default) => StreamingMultiProjectorTypesSupport.DeserializeAsync(this, projectorName, domainTypes, safeWindowThreshold, source, cancellationToken);

    private static Exception NotFound(string multiProjectorName) =>
        new($"Multi projector '{multiProjectorName}' not found");
}
