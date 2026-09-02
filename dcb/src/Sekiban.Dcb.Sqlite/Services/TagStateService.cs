using ResultBoxes;
using Sekiban.Dcb.Capabilities;
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
    private readonly IEventTypes _eventTypes;
    private readonly ITagTypes _tagTypes;
    private readonly ITagProjectorTypes _tagProjectorTypes;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public TagStateService(
        IEventStore eventStore,
        IEventTypes eventTypes,
        ITagTypes tagTypes,
        ITagProjectorTypes tagProjectorTypes,
        JsonSerializerOptions jsonSerializerOptions)
    {
        _eventStore = eventStore;
        _eventTypes = eventTypes;
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

        var projectorFunc = projectorFuncResult.GetValue();

        // Existing public operations have no cancellation token, so the optional stream intentionally receives None.
        ITagStatePayload state = new EmptyTagStatePayload();
        string? lastSortableUniqueId = null;
        var eventCount = 0;
        var taggedStream = SekibanDcbCapabilityResolver.ResolveTaggedStream(_eventStore, "event store");

        if (taggedStream.IsSupported)
        {
            string? previousId = null;
            var streamResult = await taggedStream.StreamStore!.StreamSerializableEventsByTagAsync(
                tag,
                null,
                null,
                serializableEvent =>
                {
                    if (!SekibanDcbCapabilityResolver.IsTaggedStreamOrderValid(
                            previousId,
                            serializableEvent.SortableUniqueIdValue,
                            out var duplicate))
                    {
                        throw new InvalidOperationException(
                            $"Tagged stream for {tag.GetTag()} emitted out-of-order id " +
                            $"{serializableEvent.SortableUniqueIdValue} after {previousId}.");
                    }

                    if (duplicate)
                    {
                        return ValueTask.CompletedTask;
                    }

                    var eventResult = serializableEvent.ToEvent(_eventTypes);
                    if (!eventResult.IsSuccess)
                    {
                        throw new InvalidOperationException(
                            $"Failed to deserialize event for tag {tag.GetTag()}: {eventResult.GetException().Message}",
                            eventResult.GetException());
                    }

                    state = projectorFunc(state, eventResult.GetValue());
                    eventCount++;
                    lastSortableUniqueId = serializableEvent.SortableUniqueIdValue;
                    previousId = serializableEvent.SortableUniqueIdValue;
                    return ValueTask.CompletedTask;
                },
                CancellationToken.None);
            if (!streamResult.IsSuccess)
            {
                return ResultBox.Error<TagStateProjectionResult>(streamResult.GetException());
            }
        }
        else
        {
            var eventsResult = await _eventStore.ReadEventsByTagAsync(tag, _eventTypes);
            if (!eventsResult.IsSuccess)
            {
                return ResultBox.Error<TagStateProjectionResult>(eventsResult.GetException());
            }

            foreach (var evt in eventsResult.GetValue())
            {
                state = projectorFunc(state, evt);
                eventCount++;
                lastSortableUniqueId = evt.SortableUniqueIdValue;
            }
        }

        var result = new TagStateProjectionResult(
            tag,
            projectorName,
            projectorVersion,
            state,
            eventCount,
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
