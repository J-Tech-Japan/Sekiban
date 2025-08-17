using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
    private readonly ConcurrentDictionary<string, Func<object, Event, List<ITag>, ResultBox<object>>> _projectorFunctions = new();
    private readonly ConcurrentDictionary<string, string> _projectorVersions = new();
    private readonly ConcurrentDictionary<string, Func<object>> _initialPayloadGenerators = new();
    private readonly ConcurrentDictionary<string, Type> _projectorTypes = new();

    /// <summary>
    ///     Register a multi projector type using its static GetMultiProjectorName
    /// </summary>
    public void RegisterProjector<TProjector>() 
        where TProjector : IMultiProjector<TProjector>, new()
    {
        RegisterProjector<TProjector, TProjector>();
    }

    /// <summary>
    ///     Register a multi projector type with payload type
    /// </summary>
    public void RegisterProjector<TProjector, TPayload>() 
        where TProjector : IMultiProjector<TPayload>, new()
        where TPayload : IMultiProjector<TPayload>, new()
    {
        var projectorName = TProjector.GetMultiProjectorName();
        
        // Register the projector function
        Func<object, Event, List<ITag>, ResultBox<object>> projectFunc = (payload, ev, tags) =>
        {
            if (payload is TPayload typedPayload)
            {
                var result = TProjector.Project(typedPayload, ev, tags);
                if (result.IsSuccess)
                {
                    return ResultBox.FromValue((object)result.GetValue());
                }
                return ResultBox.Error<object>(result.GetException());
            }
            return ResultBox.Error<object>(new InvalidCastException($"Payload is not of type {typeof(TPayload).Name}"));
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
        }
        
        // Register the version
        _projectorVersions[projectorName] = TProjector.GetVersion();
        
        // Register the initial payload generator
        _initialPayloadGenerators[projectorName] = () => TProjector.GenerateInitialPayload();
    }

    public ResultBox<Func<object, Event, List<ITag>, ResultBox<object>>> GetProjectorFunction(string multiProjectorName)
    {
        if (_projectorFunctions.TryGetValue(multiProjectorName, out var projectorFunc))
        {
            return ResultBox.FromValue(projectorFunc);
        }

        return ResultBox.Error<Func<object, Event, List<ITag>, ResultBox<object>>>(
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

    public ResultBox<Func<object>> GetInitialPayloadGenerator(string multiProjectorName)
    {
        if (_initialPayloadGenerators.TryGetValue(multiProjectorName, out var generator))
        {
            return ResultBox.FromValue(generator);
        }

        return ResultBox.Error<Func<object>>(
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