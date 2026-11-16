using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using System.Collections.Concurrent;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     AOT-compatible implementation of ITagProjectorTypes.
/// </summary>
public class AotTagProjectorTypes : ITagProjectorTypes
{
    private readonly ConcurrentDictionary<string, Func<ITagStatePayload, Event, ITagStatePayload>> _projectorFunctions
        = new();
    private readonly ConcurrentDictionary<string, string> _projectorVersions = new();

    /// <inheritdoc />
    public Func<ITagStatePayload, Event, ITagStatePayload> GetProjectorFunction(string tagProjectorName) =>
        _projectorFunctions.TryGetValue(tagProjectorName, out var projectorFunc)
            ? projectorFunc
            : throw new Exception($"Tag projector '{tagProjectorName}' not found");

    /// <inheritdoc />
    public string GetProjectorVersion(string tagProjectorName) =>
        _projectorVersions.TryGetValue(tagProjectorName, out var version)
            ? version
            : throw new Exception($"Tag projector '{tagProjectorName}' not found");

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllProjectorNames() => _projectorFunctions.Keys.ToList();

    /// <inheritdoc />
    public string? TryGetProjectorForTagGroup(string tagGroupName)
    {
        if (string.IsNullOrEmpty(tagGroupName))
            return null;

        // Try convention: {TagGroupName}Projector
        var conventionName = $"{tagGroupName}Projector";
        if (_projectorFunctions.ContainsKey(conventionName))
            return conventionName;

        // Try case-insensitive match for {TagGroupName}Projector
        var matchingProjector = _projectorFunctions.Keys
            .FirstOrDefault(k => k.Equals(conventionName, StringComparison.OrdinalIgnoreCase));
        if (matchingProjector != null)
            return matchingProjector;

        // Try to find any projector that starts with the tag group name
        matchingProjector = _projectorFunctions.Keys
            .FirstOrDefault(k => k.StartsWith(tagGroupName, StringComparison.OrdinalIgnoreCase));

        return matchingProjector;
    }

    /// <summary>
    ///     Register a tag projector type.
    /// </summary>
    /// <typeparam name="TProjector">The projector type to register</typeparam>
    public void RegisterProjector<TProjector>() where TProjector : ITagProjector<TProjector>
    {
        var projectorName = TProjector.ProjectorName;

        // Register the projector function
        var projectFunc = TProjector.Project;
        if (!_projectorFunctions.TryAdd(projectorName, projectFunc))
        {
            throw new InvalidOperationException($"Tag projector already registered: {projectorName}");
        }

        // Register the version
        _projectorVersions[projectorName] = TProjector.ProjectorVersion;
    }
}
