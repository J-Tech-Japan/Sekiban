using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Validation;
using System.Collections.Concurrent;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     Simple implementation of ITagProjectorTypes that manages tag projectors
/// </summary>
public class SimpleTagProjectorTypes : ITagProjectorTypes
{
    private readonly ConcurrentDictionary<string, Func<ITagStatePayload, Event, ITagStatePayload>> _projectorFunctions
        = new();
    private readonly ConcurrentDictionary<string, Type> _projectorTypes = new();
    private readonly ConcurrentDictionary<string, string> _projectorVersions = new();

    public ResultBox<Func<ITagStatePayload, Event, ITagStatePayload>> GetProjectorFunction(string tagProjectorName)
    {
        if (_projectorFunctions.TryGetValue(tagProjectorName, out var projectorFunc))
        {
            return ResultBox.FromValue(projectorFunc);
        }

        return ResultBox.Error<Func<ITagStatePayload, Event, ITagStatePayload>>(
            new Exception($"Tag projector '{tagProjectorName}' not found"));
    }

    public ResultBox<string> GetProjectorVersion(string tagProjectorName)
    {
        if (_projectorVersions.TryGetValue(tagProjectorName, out var version))
        {
            return ResultBox.FromValue(version);
        }

        return ResultBox.Error<string>(new Exception($"Tag projector '{tagProjectorName}' not found"));
    }

    public IReadOnlyList<string> GetAllProjectorNames() => _projectorFunctions.Keys.ToList();

    /// <summary>
    ///     Register a tag projector type using its static ProjectorName
    /// </summary>
    /// <typeparam name="TProjector">The projector type to register</typeparam>
    public void RegisterProjector<TProjector>() where TProjector : ITagProjector<TProjector>
    {
        var projectorName = TProjector.ProjectorName;

        // Validate projector name before registration
        NameValidator.ValidateProjectorNameAndThrow(projectorName);

        // Register the projector function
        var projectFunc = TProjector.Project;
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
                        $"Tag projector name '{projectorName}' is already registered with type '{existingTypeName}', cannot register with type '{newTypeName}'.");
                }
            }
        }
        else
        {
            // Only register type if function was successfully added
            _projectorTypes[projectorName] = typeof(TProjector);
        }

        // Register the version
        _projectorVersions[projectorName] = TProjector.ProjectorVersion;
    }
}
