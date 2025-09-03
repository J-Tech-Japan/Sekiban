using Sekiban.Dcb.MultiProjections;
using ResultBoxes;
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
public class SimpleMultiProjectorTypes : IMultiProjectorTypes
{
    private readonly ConcurrentDictionary<string, Func<IMultiProjectionPayload>> _initialPayloadGenerators = new();
    private readonly ConcurrentDictionary<string,
        Func<IMultiProjectionPayload, Event, List<ITag>, DcbDomainTypes, SortableUniqueId, ResultBox<IMultiProjectionPayload>>> _projectorFunctions = new();
    private readonly ConcurrentDictionary<string, Type> _projectorTypes = new();
    private readonly ConcurrentDictionary<string, string> _projectorVersions = new();
    private readonly ConcurrentDictionary<Type, string> _typeToNameMap = new();
    private readonly ConcurrentDictionary<string, (Func<DcbDomainTypes, string, object, byte[]> serialize, 
                                                    Func<DcbDomainTypes, string, ReadOnlySpan<byte>, object> deserialize)> _customSerializers = new();

    public ResultBox<IMultiProjectionPayload> Project(
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

        return ResultBox.Error<IMultiProjectionPayload>(
            new Exception($"Multi projector '{multiProjectorName}' not found"));
    }

    public ResultBox<string> GetProjectorVersion(string multiProjectorName)
    {
        if (_projectorVersions.TryGetValue(multiProjectorName, out var version))
        {
            return ResultBox.FromValue(version);
        }

        return ResultBox.Error<string>(new Exception($"Multi projector '{multiProjectorName}' not found"));
    }


    public ResultBox<IMultiProjectionPayload> GenerateInitialPayload(string multiProjectorName)
    {
        if (_initialPayloadGenerators.TryGetValue(multiProjectorName, out var generator))
        {
            return ResultBox.FromValue(generator());
        }

        return ResultBox.Error<IMultiProjectionPayload>(
            new Exception($"Multi projector '{multiProjectorName}' not found"));
    }


    public ResultBox<IMultiProjectionPayload> Deserialize(
        byte[] data,
        string multiProjectorName,
        JsonSerializerOptions jsonOptions)
    {
        try
        {
            // Get the projector type from the multiProjectorName
            if (!_projectorTypes.TryGetValue(multiProjectorName, out var projectorType))
            {
                return ResultBox.Error<IMultiProjectionPayload>(
                    new Exception($"Multi projector '{multiProjectorName}' not found"));
            }

            // Since TProjector and TPayload are the same type now, use the projector type directly
            var json = Encoding.UTF8.GetString(data);
            var result = JsonSerializer.Deserialize(json, projectorType, jsonOptions);
            if (result is IMultiProjectionPayload payload)
            {
                return ResultBox.FromValue(payload);
            }

            return ResultBox.Error<IMultiProjectionPayload>(
                new Exception("Deserialized object is not an IMultiProjectionPayload"));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IMultiProjectionPayload>(ex);
        }
    }

    public ResultBox<Func<IMultiProjectionPayload>> GetInitialPayloadGenerator(string multiProjectorName)
    {
        if (_initialPayloadGenerators.TryGetValue(multiProjectorName, out var generator))
        {
            return ResultBox.FromValue(generator);
        }

        return ResultBox.Error<Func<IMultiProjectionPayload>>(
            new Exception($"Multi projector '{multiProjectorName}' not found"));
    }

    public ResultBox<Type> GetProjectorType(string multiProjectorName)
    {
        if (_projectorTypes.TryGetValue(multiProjectorName, out var type))
        {
            return ResultBox.FromValue(type);
        }

        return ResultBox.Error<Type>(new Exception($"Multi projector '{multiProjectorName}' not found"));
    }

    /// <summary>
    ///     Register a multi projector type using its static GetMultiProjectorName
    /// </summary>
    public void RegisterProjector<TProjector>() where TProjector : IMultiProjector<TProjector>, new()
    {
        var projectorName = TProjector.MultiProjectorName;

        // Register the projector function
    Func<IMultiProjectionPayload, Event, List<ITag>, DcbDomainTypes, SortableUniqueId, ResultBox<IMultiProjectionPayload>> projectFunc
        = (payload, ev, tags, domainTypes, safeWindowThreshold) =>
            {
                if (payload is TProjector typedPayload)
                {
            var result = TProjector.Project(typedPayload, ev, tags, domainTypes, safeWindowThreshold);
                    if (result.IsSuccess)
                    {
                        return ResultBox.FromValue((IMultiProjectionPayload)result.GetValue());
                    }
                    return ResultBox.Error<IMultiProjectionPayload>(result.GetException());
                }
                return ResultBox.Error<IMultiProjectionPayload>(
                    new InvalidCastException($"Payload is not of type {typeof(TProjector).Name}"));
            };

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
        } else
        {
            // Only register if function was successfully added
            _projectorTypes[projectorName] = typeof(TProjector);
            _typeToNameMap[typeof(TProjector)] = projectorName;
        }

        // Register the version
        _projectorVersions[projectorName] = TProjector.MultiProjectorVersion;

        // Register the initial payload generator
        _initialPayloadGenerators[projectorName] = () => TProjector.GenerateInitialPayload();
    }
    
    /// <summary>
    ///     Serializes a multi-projection payload to JSON string.
    ///     Uses custom serialization if registered, otherwise falls back to standard JSON serialization.
    /// </summary>
    public ResultBox<byte[]> Serialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        IMultiProjectionPayload payload)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(safeWindowThreshold))
            {
                return ResultBox.Error<byte[]>(new ArgumentException("safeWindowThreshold must be supplied"));
            }
            if (_customSerializers.TryGetValue(projectorName, out var serializers))
            {
                return ResultBox.FromValue(serializers.serialize(domainTypes, safeWindowThreshold, payload));
            }
            
            // Fallback: JSON serialize then gzip compress (Fastest)
            var json = JsonSerializer.Serialize(payload, payload.GetType(), domainTypes.JsonSerializerOptions);
            var utf8 = Encoding.UTF8.GetBytes(json);
            var compressed = GzipCompression.Compress(utf8);
            return ResultBox.FromValue(compressed);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<byte[]>(ex);
        }
    }
    
    /// <summary>
    ///     Deserializes a JSON string to a multi-projection payload.
    ///     Uses custom deserialization if registered, otherwise falls back to standard JSON deserialization.
    /// </summary>
    public ResultBox<IMultiProjectionPayload> Deserialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        byte[] data)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(safeWindowThreshold))
            {
                return ResultBox.Error<IMultiProjectionPayload>(new ArgumentException("safeWindowThreshold must be supplied"));
            }
            if (_customSerializers.TryGetValue(projectorName, out var serializers))
            {
                return ResultBox.FromValue((IMultiProjectionPayload)serializers.deserialize(domainTypes, safeWindowThreshold, data));
            }
            
            // Fallback: assume gzip-compressed JSON
            var jsonBytes = GzipCompression.Decompress(data);
            var json = Encoding.UTF8.GetString(jsonBytes);
            if (_projectorTypes.TryGetValue(projectorName, out var type))
            {
                var result = JsonSerializer.Deserialize(json, type, domainTypes.JsonSerializerOptions) as IMultiProjectionPayload;
                if (result != null)
                {
                    return ResultBox.FromValue(result);
                }
                return ResultBox.Error<IMultiProjectionPayload>(new InvalidOperationException($"Failed to deserialize to {type.Name}"));
            }
            
            return ResultBox.Error<IMultiProjectionPayload>(
                new KeyNotFoundException($"Projector '{projectorName}' not found"));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IMultiProjectionPayload>(ex);
        }
    }
    
    /// <summary>
    ///     Registers a projector with custom serialization support.
    ///     The projector must implement IMultiProjectorWithCustomSerialization interface.
    /// </summary>
    public ResultBox<bool> RegisterProjectorWithCustomSerialization<T>()
        where T : IMultiProjectorWithCustomSerialization<T>, new()
    {
        try
        {
            var projectorName = T.MultiProjectorName;
            var type = typeof(T);
            
            // Store custom serialization delegates
            _customSerializers[projectorName] = (
                serialize: (domain, safeWindowThreshold, payload) => T.Serialize(domain, safeWindowThreshold, (T)payload),
                deserialize: (domain, safeWindowThreshold, data) => T.Deserialize(domain, data)
            );
            
            // Register the projector using the base RegisterProjector method
            // Since IMultiProjectorWithCustomSerialization<T> inherits from IMultiProjector<T>,
            // we can directly call RegisterProjector
            RegisterProjector<T>();
            
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }
}
