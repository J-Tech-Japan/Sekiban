using System;
using System.Collections.Concurrent;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     Simple in-memory registry for multi projectors.
/// </summary>
public class SimpleMultiProjectorTypes : IMultiProjectorTypes
{
    private readonly ConcurrentDictionary<string, IMultiProjectorCommon> _projectors = new(StringComparer.Ordinal);

    public void RegisterProjector<TProjector>(string? name = null)
        where TProjector : IMultiProjectorCommon, new()
    {
        var key = name ?? typeof(TProjector).Name;
        _projectors[key] = new TProjector();
    }

    public void RegisterProjector<TProjector, TPayload>(string? name = null)
        where TProjector : IMultiProjector<TPayload>, new()
        where TPayload : notnull
    {
        var key = name ?? TProjector.GetMultiProjectorName();
        _projectors[key] = new TProjector();
    }

    public ResultBox<IMultiProjectorCommon> GetMultiProjector(string multiProjectorName)
    {
        if (_projectors.TryGetValue(multiProjectorName, out var projector))
        {
            return ResultBox.FromValue(projector);
        }
        return ResultBox.Error<IMultiProjectorCommon>(new Exception($"Multi projector '{multiProjectorName}' not found"));
    }
}
