using System.Reflection;
using Sekiban.Dcb.Tags;
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
    /// Register a tag projector
    /// </summary>
    public void RegisterProjector(ITagProjector projector)
    {
        _projectors[projector.GetTagProjectorName()] = projector;
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