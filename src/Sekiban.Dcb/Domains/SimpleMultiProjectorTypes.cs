using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     Simple in-memory registry for multi projectors.
/// </summary>
public class SimpleMultiProjectorTypes : IMultiProjectorTypes
{
    private readonly ConcurrentDictionary<string, Func<IMultiProjectionPayload, Event, List<ITag>, ResultBox<IMultiProjectionPayload>>> _projectorFunctions = new();
    private readonly ConcurrentDictionary<string, string> _projectorVersions = new();
    private readonly ConcurrentDictionary<string, Func<IMultiProjectionPayload>> _initialPayloadGenerators = new();
    private readonly ConcurrentDictionary<string, Type> _projectorTypes = new();
    private readonly ConcurrentDictionary<Type, string> _typeToNameMap = new();

    /// <summary>
    ///     Register a multi projector type using its static GetMultiProjectorName
    /// </summary>
    public void RegisterProjector<TProjector>() 
        where TProjector : IMultiProjector<TProjector>, new()
    {
        var projectorName = TProjector.GetMultiProjectorName();
        
        // Register the projector function
        Func<IMultiProjectionPayload, Event, List<ITag>, ResultBox<IMultiProjectionPayload>> projectFunc = (payload, ev, tags) =>
        {
            if (payload is TProjector typedPayload)
            {
                var result = TProjector.Project(typedPayload, ev, tags);
                if (result.IsSuccess)
                {
                    return ResultBox.FromValue((IMultiProjectionPayload)result.GetValue());
                }
                return ResultBox.Error<IMultiProjectionPayload>(result.GetException());
            }
            return ResultBox.Error<IMultiProjectionPayload>(new InvalidCastException($"Payload is not of type {typeof(TProjector).Name}"));
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
        }
        else
        {
            // Only register if function was successfully added
            _projectorTypes[projectorName] = typeof(TProjector);
            _typeToNameMap[typeof(TProjector)] = projectorName;
        }
        
        // Register the version
        _projectorVersions[projectorName] = TProjector.GetVersion();
        
        // Register the initial payload generator
        _initialPayloadGenerators[projectorName] = () => (IMultiProjectionPayload)TProjector.GenerateInitialPayload();
    }

    public ResultBox<IMultiProjectionPayload> Project(string multiProjectorName, IMultiProjectionPayload payload, Event ev, List<ITag> tags)
    {
        if (_projectorFunctions.TryGetValue(multiProjectorName, out var projectorFunc))
        {
            return projectorFunc(payload, ev, tags);
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

        return ResultBox.Error<string>(
            new Exception($"Multi projector '{multiProjectorName}' not found"));
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

    
    public ResultBox<IMultiProjectionPayload> Deserialize(byte[] data, string multiProjectorName, System.Text.Json.JsonSerializerOptions jsonOptions)
    {
        try
        {
            // Get the projector type from the multiProjectorName
            if (!_projectorTypes.TryGetValue(multiProjectorName, out var projectorType))
            {
                return ResultBox.Error<IMultiProjectionPayload>(new Exception($"Multi projector '{multiProjectorName}' not found"));
            }
            
            // Since TProjector and TPayload are the same type now, use the projector type directly
            var json = System.Text.Encoding.UTF8.GetString(data);
            var result = System.Text.Json.JsonSerializer.Deserialize(json, projectorType, jsonOptions);
            if (result is IMultiProjectionPayload payload)
            {
                return ResultBox.FromValue(payload);
            }
            
            return ResultBox.Error<IMultiProjectionPayload>(new Exception($"Deserialized object is not an IMultiProjectionPayload"));
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

        return ResultBox.Error<Type>(
            new Exception($"Multi projector '{multiProjectorName}' not found"));
    }
}