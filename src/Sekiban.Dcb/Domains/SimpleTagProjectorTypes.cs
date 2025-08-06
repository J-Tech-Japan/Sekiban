using System.Reflection;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Events;
using ResultBoxes;

namespace Sekiban.Dcb.Domains;

/// <summary>
/// Simple implementation of ITagProjectorTypes that manages tag projectors
/// </summary>
public class SimpleTagProjectorTypes : ITagProjectorTypes
{
    private readonly Dictionary<string, ITagProjector> _projectors;
    
    public SimpleTagProjectorTypes()
    {
        _projectors = new Dictionary<string, ITagProjector>();
    }
    
    /// <summary>
    /// Register a tag projector type
    /// </summary>
    /// <param name="name">Optional name for the projector. If null, uses the type name</param>
    public void RegisterProjector<T>(string? name = null) where T : ITagProjector, new()
    {
        var projector = new T();
        var projectorName = name ?? typeof(T).Name;
        _projectors[projectorName] = projector;
    }
    
    public ResultBox<ITagProjector> GetTagProjector(string tagProjectorName)
    {
        if (_projectors.TryGetValue(tagProjectorName, out var projector))
        {
            return ResultBox.FromValue(projector);
        }
        
        return ResultBox.Error<ITagProjector>(new Exception($"Tag projector '{tagProjectorName}' not found"));
    }
}