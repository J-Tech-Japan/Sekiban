using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text.Json;
namespace Sekiban.Dcb.Services;

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
    public Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(string tagString, string projectorName) =>
        ProjectTagStateAsync(tagString, projectorName, CancellationToken.None);

    /// <summary>
    ///     Project events for a tag using a specified projector with optional cooperative cancellation.
    /// </summary>
    public async Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(
        string tagString,
        string projectorName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tag = ParseTag(tagString);
        cancellationToken.ThrowIfCancellationRequested();
        return await ProjectTagStateAsync(tag, projectorName, cancellationToken);
    }

    /// <summary>
    ///     Project events for a tag, automatically inferring the projector from the tag group name.
    ///     Tries "{TagGroupName}Projector" convention.
    /// </summary>
    /// <param name="tagString">Tag string in format 'group:content'</param>
    /// <returns>Projected tag state result</returns>
    public Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(string tagString) =>
        ProjectTagStateAsync(tagString, CancellationToken.None);

    /// <summary>
    ///     Project events for a tag inferred from its tag group with optional cooperative cancellation.
    /// </summary>
    public async Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(
        string tagString,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tag = ParseTag(tagString);
        cancellationToken.ThrowIfCancellationRequested();
        return await ProjectTagStateAsync(tag, cancellationToken);
    }

    /// <summary>
    ///     Project events for a tag, automatically inferring the projector from the tag group name.
    ///     Tries "{TagGroupName}Projector" convention.
    /// </summary>
    /// <param name="tag">The tag to project</param>
    /// <returns>Projected tag state result</returns>
    public Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(ITag tag) =>
        ProjectTagStateAsync(tag, CancellationToken.None);

    /// <summary>
    ///     Project events for a tag inferred from its tag group with optional cooperative cancellation.
    /// </summary>
    public async Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(
        ITag tag,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        cancellationToken.ThrowIfCancellationRequested();
        return await ProjectTagStateAsync(tag, projectorName, cancellationToken);
    }

    /// <summary>
    ///     Project events for a tag using a specified projector
    /// </summary>
    /// <param name="tag">The tag to project</param>
    /// <param name="projectorName">Name of the tag projector to use</param>
    /// <returns>Projected tag state result</returns>
    public Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(ITag tag, string projectorName) =>
        ProjectTagStateAsync(tag, projectorName, CancellationToken.None);

    /// <summary>
    ///     Project events for a tag using a specified projector with optional cooperative cancellation.
    /// </summary>
    public async Task<ResultBox<TagStateProjectionResult>> ProjectTagStateAsync(
        ITag tag,
        string projectorName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        // Project all events. The legacy overload enters here with CancellationToken.None.
        ITagStatePayload state = new EmptyTagStatePayload();
        string? lastSortableUniqueId = null;
        var eventCount = 0;
        var taggedStream = SekibanDcbCapabilityResolver.ResolveTaggedStream(_eventStore, "event store");

        if (taggedStream.IsSupported)
        {
            var streamResult = await TaggedStreamProjectionHelper.ProjectAsync(
                taggedStream.StreamStore!,
                tag,
                null,
                null,
                _eventTypes,
                projectorFunc,
                state,
                null,
                string.Empty,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!streamResult.IsSuccess)
            {
                if (streamResult.GetException() is OperationCanceledException cancellationException)
                {
                    throw cancellationException;
                }

                return ResultBox.Error<TagStateProjectionResult>(streamResult.GetException());
            }

            var streamProjection = streamResult.GetValue();
            state = streamProjection.State;
            eventCount = streamProjection.EventCount;
            lastSortableUniqueId = streamProjection.LastSortableUniqueId;
        }
        else
        {
            var eventsResult = await _eventStore.ReadEventsByTagAsync(tag, _eventTypes);
            cancellationToken.ThrowIfCancellationRequested();
            if (!eventsResult.IsSuccess)
            {
                return ResultBox.Error<TagStateProjectionResult>(eventsResult.GetException());
            }

            foreach (var evt in eventsResult.GetValue())
            {
                cancellationToken.ThrowIfCancellationRequested();
                state = projectorFunc(state, evt);
                eventCount++;
                lastSortableUniqueId = evt.SortableUniqueIdValue;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
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
