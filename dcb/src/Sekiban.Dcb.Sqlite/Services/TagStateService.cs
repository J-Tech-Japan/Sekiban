using ResultBoxes;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text.Json;

namespace Sekiban.Dcb.Sqlite.Services;

/// <summary>
///     Result of projecting a tag state
/// </summary>
public record TagStateProjectionResult(
    ITag Tag,
    string ProjectorName,
    string ProjectorVersion,
    ITagStatePayload State,
    int EventCount,
    string? LastSortableUniqueId);

/// <summary>
///     Service for getting and projecting tag states
/// </summary>
public class TagStateService
{
    private readonly IEventStore _eventStore;
    private readonly ITagTypes _tagTypes;
    private readonly ITagProjectorTypes _tagProjectorTypes;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public TagStateService(
        IEventStore eventStore,
        ITagTypes tagTypes,
        ITagProjectorTypes tagProjectorTypes,
        JsonSerializerOptions jsonSerializerOptions)
    {
        _eventStore = eventStore;
        _tagTypes = tagTypes;
        _tagProjectorTypes = tagProjectorTypes;
        _jsonSerializerOptions = jsonSerializerOptions;
    }

    /// <summary>
    ///     Parse a tag string into an ITag instance
    /// </summary>
    public ITag ParseTag(string tagString) => _tagTypes.GetTag(tagString);

    /// <summary>
    ///     Get the latest stored tag state from the event store
    /// </summary>
    public Task<ResultBox<TagState>> GetLatestTagStateAsync(ITag tag)
        => _eventStore.GetLatestTagAsync(tag);

    /// <summary>
    ///     Get the latest stored tag state by tag string
    /// </summary>
    public Task<ResultBox<TagState>> GetLatestTagStateByStringAsync(string tagString)
    {
        var tag = ParseTag(tagString);
        return GetLatestTagStateAsync(tag);
    }

    /// <summary>
    ///     Project events for a tag using a specified projector
    /// </summary>
    /// <param name="tagString">Tag string in format 'group:content'</param>
    /// <param name="projectorName">Name of the tag projector to use</param>
    /// <returns>Projected tag state result</returns>
    public async Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(string tagString, string projectorName)
    {
        var tag = ParseTag(tagString);
        return await ProjectTagStateAsync(tag, projectorName);
    }

    /// <summary>
    ///     Project events for a tag, automatically inferring the projector from the tag group name.
    ///     Tries "{TagGroupName}Projector" convention.
    /// </summary>
    /// <param name="tagString">Tag string in format 'group:content'</param>
    /// <returns>Projected tag state result</returns>
    public async Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(string tagString)
    {
        var tag = ParseTag(tagString);
        return await ProjectTagStateAsync(tag);
    }

    /// <summary>
    ///     Project events for a tag, automatically inferring the projector from the tag group name.
    ///     Tries "{TagGroupName}Projector" convention.
    /// </summary>
    /// <param name="tag">The tag to project</param>
    /// <returns>Projected tag state result</returns>
    public async Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(ITag tag)
    {
        var tagGroup = tag.GetTagGroup();
        var projectorName = _tagProjectorTypes.TryGetProjectorForTagGroup(tagGroup);

        if (projectorName == null)
        {
            return ResultBox.Error<TagStateProjectionResult>(
                new InvalidOperationException(
                    $"Could not find a projector for tag group '{tagGroup}'. " +
                    $"Tried '{tagGroup}Projector'. " +
                    $"Available projectors: {string.Join(", ", GetAllTagProjectorNames())}"));
        }

        return await ProjectTagStateAsync(tag, projectorName);
    }

    /// <summary>
    ///     Project events for a tag using a specified projector
    /// </summary>
    /// <param name="tag">The tag to project</param>
    /// <param name="projectorName">Name of the tag projector to use</param>
    /// <returns>Projected tag state result</returns>
    public async Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(ITag tag, string projectorName)
    {
        // Get the projector function
        var projectorFuncResult = _tagProjectorTypes.GetProjectorFunction(projectorName);
        if (!projectorFuncResult.IsSuccess)
        {
            return ResultBox.Error<TagStateProjectionResult>(projectorFuncResult.GetException());
        }

        // Get the projector version
        var projectorVersionResult = _tagProjectorTypes.GetProjectorVersion(projectorName);
        var projectorVersion = projectorVersionResult.IsSuccess ? projectorVersionResult.GetValue() : "unknown";

        // Fetch events for the tag
        var eventsResult = await _eventStore.ReadEventsByTagAsync(tag);
        if (!eventsResult.IsSuccess)
        {
            return ResultBox.Error<TagStateProjectionResult>(eventsResult.GetException());
        }

        var events = eventsResult.GetValue().ToList();
        var projectorFunc = projectorFuncResult.GetValue();

        // Project all events
        ITagStatePayload state = new EmptyTagStatePayload();
        string? lastSortableUniqueId = null;

        foreach (var evt in events)
        {
            state = projectorFunc(state, evt);
            lastSortableUniqueId = evt.SortableUniqueIdValue;
        }

        var result = new TagStateProjectionResult(
            tag,
            projectorName,
            projectorVersion,
            state,
            events.Count,
            lastSortableUniqueId);

        return ResultBox.FromValue(result);
    }

    /// <summary>
    ///     Get all registered tag projector names
    /// </summary>
    public IReadOnlyList<string> GetAllTagProjectorNames()
        => _tagProjectorTypes.GetAllProjectorNames();

    /// <summary>
    ///     Get all registered tag group names
    /// </summary>
    public IReadOnlyList<string> GetAllTagGroupNames()
        => _tagTypes.GetAllTagGroupNames();

    /// <summary>
    ///     Get the JSON serializer options from domain types
    /// </summary>
    public System.Text.Json.JsonSerializerOptions JsonSerializerOptions => _jsonSerializerOptions;
}
