using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text.Json;
using CoreTagEventService = Sekiban.Dcb.Services.TagEventService;

namespace Sekiban.Dcb.Sqlite.Services;

/// <summary>
///     Service for fetching events by tag. SQLite's historical service surface; the behaviour belongs to the core
///     <see cref="Sekiban.Dcb.Services.TagEventService" />.
/// </summary>
public class TagEventService
{
    private readonly CoreTagEventService _inner;

    public TagEventService(
        IEventStore eventStore,
        IEventTypes eventTypes,
        ITagTypes tagTypes,
        JsonSerializerOptions jsonSerializerOptions)
    {
        _inner = new CoreTagEventService(eventStore, eventTypes, tagTypes, jsonSerializerOptions);
    }

    /// <inheritdoc cref="CoreTagEventService.ParseTag" />
    public ITag ParseTag(string tagString) => _inner.ParseTag(tagString);

    /// <inheritdoc cref="CoreTagEventService.GetEventsByTagAsync" />
    public Task<IEnumerable<Event>> GetEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
        _inner.GetEventsByTagAsync(tag, since);

    /// <inheritdoc cref="CoreTagEventService.GetEventsByTagStringAsync" />
    public Task<IEnumerable<Event>> GetEventsByTagStringAsync(string tagString, SortableUniqueId? since = null) =>
        _inner.GetEventsByTagStringAsync(tagString, since);

    /// <inheritdoc cref="CoreTagEventService.JsonSerializerOptions" />
    public JsonSerializerOptions JsonSerializerOptions => _inner.JsonSerializerOptions;
}
