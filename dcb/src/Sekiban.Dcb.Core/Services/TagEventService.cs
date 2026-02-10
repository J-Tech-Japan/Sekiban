using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text.Json;
namespace Sekiban.Dcb.Services;

/// <summary>
///     Service for fetching events by tag
/// </summary>
public class TagEventService
{
    private readonly IEventStore _eventStore;
    private readonly ITagTypes _tagTypes;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public TagEventService(IEventStore eventStore, ITagTypes tagTypes, JsonSerializerOptions jsonSerializerOptions)
    {
        _eventStore = eventStore;
        _tagTypes = tagTypes;
        _jsonSerializerOptions = jsonSerializerOptions;
    }

    /// <summary>
    ///     Parse a tag string into an ITag instance
    /// </summary>
    /// <param name="tagString">Tag string in format 'group:content'</param>
    /// <returns>Parsed tag</returns>
    public ITag ParseTag(string tagString) => _tagTypes.GetTag(tagString);

    /// <summary>
    ///     Fetch all events for a specific tag
    /// </summary>
    /// <param name="tag">The tag to fetch events for</param>
    /// <param name="since">Optional: Only return events after this ID</param>
    /// <returns>List of events for the tag</returns>
    public Task<ResultBox<IEnumerable<Event>>> GetEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
        => _eventStore.ReadEventsByTagAsync(tag, since);

    /// <summary>
    ///     Fetch all events for a tag specified by string
    /// </summary>
    /// <param name="tagString">Tag string in format 'group:content'</param>
    /// <param name="since">Optional: Only return events after this ID</param>
    /// <returns>List of events for the tag</returns>
    public Task<ResultBox<IEnumerable<Event>>> GetEventsByTagStringAsync(string tagString, SortableUniqueId? since = null)
    {
        var tag = ParseTag(tagString);
        return GetEventsByTagAsync(tag, since);
    }

    /// <summary>
    ///     Get the JSON serializer options from domain types
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions => _jsonSerializerOptions;
}
