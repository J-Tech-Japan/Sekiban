using ResultBoxes;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Validation;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     Simple implementation of ITagProjectorTypes that manages tag projectors
/// </summary>
public class SimpleTagProjectorTypes : ITagProjectorTypes
{
    private readonly Dictionary<string, ITagProjector> _projectors;
    private readonly Dictionary<Type, string> _typeToNameMapping;

    public SimpleTagProjectorTypes()
    {
        _projectors = new Dictionary<string, ITagProjector>();
        _typeToNameMapping = new Dictionary<Type, string>();
    }

    public ResultBox<ITagProjector> GetTagProjector(string tagProjectorName)
    {
        if (_projectors.TryGetValue(tagProjectorName, out var projector))
        {
            return ResultBox.FromValue(projector);
        }

        return ResultBox.Error<ITagProjector>(new Exception($"Tag projector '{tagProjectorName}' not found"));
    }

    /// <summary>
    ///     Register a tag projector type
    /// </summary>
    /// <param name="name">Optional name for the projector. If null, uses the type name</param>
    public void RegisterProjector<T>(string? name = null) where T : ITagProjector, new()
    {
        var projector = new T();
        var projectorName = name ?? typeof(T).Name;
        
        // Validate projector name before registration
        NameValidator.ValidateProjectorNameAndThrow(projectorName);
        
        var newType = typeof(T);

        if (_projectors.TryGetValue(projectorName, out var existingProjector))
        {
            var existingType = existingProjector.GetType();
            if (existingType != newType)
            {
                throw new InvalidOperationException(
                    $"Tag projector name '{projectorName}' is already registered with type '{existingType.FullName}'. " +
                    $"Cannot register it with different type '{newType.FullName}'.");
            }
        }
        _projectors[projectorName] = projector;
        _typeToNameMapping[newType] = projectorName;
    }
    
    /// <summary>
    ///     Gets the registered name for a projector type
    /// </summary>
    public string? GetProjectorName(Type projectorType)
    {
        return _typeToNameMapping.TryGetValue(projectorType, out var name) ? name : null;
    }
}
