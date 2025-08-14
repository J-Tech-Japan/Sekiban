using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     Simple in-memory registry for multi projectors.
/// </summary>
public class SimpleMultiProjectorTypes : IMultiProjectorTypes
{
    private readonly ConcurrentDictionary<string, IMultiProjectorCommon> _projectors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Type> _projectorTypes = new(StringComparer.Ordinal);

    public void RegisterProjector<TProjector>(string? name = null)
        where TProjector : IMultiProjectorCommon, new()
    {
        var key = name ?? typeof(TProjector).Name;
    _projectors[key] = new TProjector();
    _projectorTypes[key] = typeof(TProjector);
    }

    public void RegisterProjector<TProjector, TPayload>(string? name = null)
        where TProjector : IMultiProjector<TPayload>, new()
        where TPayload : notnull
    {
        var key = name ?? TProjector.GetMultiProjectorName();
    _projectors[key] = new TProjector();
    _projectorTypes[key] = typeof(TProjector);
    }

    public ResultBox<IMultiProjectorCommon> GetMultiProjector(string multiProjectorName)
    {
        if (_projectors.TryGetValue(multiProjectorName, out var projector))
        {
            return ResultBox.FromValue(projector);
        }
        return ResultBox.Error<IMultiProjectorCommon>(new Exception($"Multi projector '{multiProjectorName}' not found"));
    }

    public ResultBox<IMultiProjectorCommon> Project(IMultiProjectorCommon multiProjector, Event ev)
    {
        try
        {
            dynamic dyn = multiProjector;
            dynamic result = dyn.Project(dyn, ev); // IMultiProjector<T>.Project(T, Event) -> ResultBox<T>
            if (result.IsSuccess)
            {
                IMultiProjectorCommon value = (IMultiProjectorCommon)result.GetValue();
                return ResultBox.FromValue(value);
            }
            return ResultBox.Error<IMultiProjectorCommon>((Exception)result.GetException());
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IMultiProjectorCommon>(ex);
        }
    }

    public ResultBox<IMultiProjectorCommon> GenerateInitialPayload(string multiProjectorName)
    {
        if (!_projectorTypes.TryGetValue(multiProjectorName, out var type))
        {
            return ResultBox.Error<IMultiProjectorCommon>(new Exception($"Multi projector type '{multiProjectorName}' not registered"));
        }
        try
        {
            // Try to call static abstract GenerateInitialPayload via reflection
            var method = type.GetMethod("GenerateInitialPayload");
            if (method == null)
            {
                return ResultBox.Error<IMultiProjectorCommon>(new MissingMethodException(type.FullName, "GenerateInitialPayload"));
            }
            var payload = method.Invoke(null, null);
            if (payload is IMultiProjectorCommon projector)
            {
                return ResultBox.FromValue(projector);
            }
            return ResultBox.Error<IMultiProjectorCommon>(new InvalidOperationException("GenerateInitialPayload returned invalid type"));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IMultiProjectorCommon>(ex);
        }
    }

    public ResultBox<byte[]> Serialize(IMultiProjectorCommon multiProjector, JsonSerializerOptions options)
    {
        try
        {
            var type = multiProjector.GetType();
            var json = JsonSerializer.SerializeToUtf8Bytes(multiProjector, type, options);
            return ResultBox.FromValue(json);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<byte[]>(ex);
        }
    }

    public ResultBox<IMultiProjectorCommon> Deserialize(byte[] jsonBytes, string payloadTypeFullName, JsonSerializerOptions options)
    {
        try
        {
            var type = _projectorTypes.Values.FirstOrDefault(t => t.FullName == payloadTypeFullName);
            if (type == null)
            {
                return ResultBox.Error<IMultiProjectorCommon>(new Exception($"Projector type '{payloadTypeFullName}' not registered"));
            }
            var obj = JsonSerializer.Deserialize(jsonBytes, type, options);
            if (obj is IMultiProjectorCommon projector)
            {
                return ResultBox.FromValue(projector);
            }
            return ResultBox.Error<IMultiProjectorCommon>(new InvalidOperationException("Deserialized object is not IMultiProjectorCommon"));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IMultiProjectorCommon>(ex);
        }
    }

    public ResultBox<string> GetMultiProjectorNameFromMultiProjector(IMultiProjectorCommon multiProjector)
    {
        try
        {
            var type = multiProjector.GetType();
            var method = type.GetMethod("GetMultiProjectorName");
            if (method == null)
            {
                return ResultBox.Error<string>(new MissingMethodException(type.FullName, "GetMultiProjectorName"));
            }
            var name = method.Invoke(null, null) as string;
            return name != null ? ResultBox.FromValue(name) : ResultBox.Error<string>(new InvalidOperationException("GetMultiProjectorName returned null"));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<string>(ex);
        }
    }
}
