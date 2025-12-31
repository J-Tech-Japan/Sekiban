using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     CosmosDB-backed event store implementation.
/// </summary>
public class CosmosDbEventStore : IEventStore
{
    private readonly CosmosDbContext _context;
    private readonly DcbDomainTypes _domainTypes;

    /// <summary>
    ///     Creates a new CosmosDB event store.
    /// </summary>
    public CosmosDbEventStore(CosmosDbContext context, DcbDomainTypes domainTypes)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
    }

    /// <summary>
    ///     Reads all events, optionally after a given sortable unique ID.
    /// </summary>
    public async Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null)
    {
        try
        {
            var container = await _context.GetEventsContainerAsync().ConfigureAwait(false);

            var query = container.GetItemLinqQueryable<CosmosEvent>()
                .OrderBy(e => e.SortableUniqueId);

            if (since != null)
            {
                // Use CompareTo instead of string.Compare for Cosmos DB LINQ compatibility
                query = query.Where(e => e.SortableUniqueId.CompareTo(since.Value) > 0)
                    .OrderBy(e => e.SortableUniqueId);
            }

            var events = new List<Event>();
            using var iterator = query.ToFeedIterator();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync().ConfigureAwait(false);
                foreach (var cosmosEvent in response)
                {
                    var payloadResult = DeserializeEventPayload(cosmosEvent.EventType, cosmosEvent.Payload);
                    if (!payloadResult.IsSuccess)
                    {
                        return ResultBox.Error<IEnumerable<Event>>(payloadResult.GetException());
                    }

                    events.Add(cosmosEvent.ToEvent(payloadResult.GetValue()));
                }
            }

            return ResultBox.FromValue<IEnumerable<Event>>(events);
        }
        catch (CosmosException ex)
        {
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
        catch (JsonException ex)
        {
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
        catch (InvalidOperationException ex)
        {
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
        catch (ArgumentException ex)
        {
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
    }

    /// <summary>
    ///     Reads events by tag, optionally after a given sortable unique ID.
    /// </summary>
    public async Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        try
        {
            var tagString = tag.GetTag();

            // First, get all event IDs for this tag from the tags container
            var tagsContainer = await _context.GetTagsContainerAsync().ConfigureAwait(false);
            var tagQuery = tagsContainer.GetItemLinqQueryable<CosmosTag>()
                .Where(t => t.Tag == tagString);

            if (since != null)
            {
                // Use CompareTo instead of string.Compare for Cosmos DB LINQ compatibility
                tagQuery = tagQuery.Where(t => t.SortableUniqueId.CompareTo(since.Value) > 0);
            }

            var eventIds = new List<string>();
            using (var tagIterator = tagQuery.OrderBy(t => t.SortableUniqueId).ToFeedIterator())
            {
                while (tagIterator.HasMoreResults)
                {
                    var response = await tagIterator.ReadNextAsync().ConfigureAwait(false);
                    eventIds.AddRange(response.Select(t => t.EventId));
                }
            }

            if (eventIds.Count == 0)
            {
                return ResultBox.FromValue<IEnumerable<Event>>(new List<Event>());
            }

            // Now fetch the events by their IDs
            var eventsContainer = await _context.GetEventsContainerAsync().ConfigureAwait(false);
            var events = new List<Event>();

            // Batch read events for better performance
            var tasks = eventIds.Select(async eventId =>
            {
                try
                {
                    var response = await eventsContainer.ReadItemAsync<CosmosEvent>(
                        eventId,
                        new PartitionKey(eventId)).ConfigureAwait(false);

                    return response.Resource;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // Event might have been deleted, skip it
                    return null;
                }
            });

            var cosmosEvents = await Task.WhenAll(tasks).ConfigureAwait(false);

            foreach (var cosmosEvent in cosmosEvents.Where(e => e != null))
            {
                var payloadResult = DeserializeEventPayload(cosmosEvent!.EventType, cosmosEvent.Payload);
                if (!payloadResult.IsSuccess)
                {
                    return ResultBox.Error<IEnumerable<Event>>(payloadResult.GetException());
                }

                events.Add(cosmosEvent.ToEvent(payloadResult.GetValue()));
            }

            // Sort events by SortableUniqueId
            events = events.OrderBy(e => e.SortableUniqueIdValue).ToList();

            return ResultBox.FromValue<IEnumerable<Event>>(events);
        }
        catch (CosmosException ex)
        {
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
        catch (JsonException ex)
        {
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
        catch (InvalidOperationException ex)
        {
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
        catch (ArgumentException ex)
        {
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
    }

    /// <summary>
    ///     Reads a single event by ID.
    /// </summary>
    public async Task<ResultBox<Event>> ReadEventAsync(Guid eventId)
    {
        try
        {
            var eventIdStr = eventId.ToString();
            var container = await _context.GetEventsContainerAsync().ConfigureAwait(false);

            var response = await container.ReadItemAsync<CosmosEvent>(
                eventIdStr,
                new PartitionKey(eventIdStr)).ConfigureAwait(false);

            var cosmosEvent = response.Resource;

            var payloadResult = DeserializeEventPayload(cosmosEvent.EventType, cosmosEvent.Payload);
            if (!payloadResult.IsSuccess)
            {
                return ResultBox.Error<Event>(payloadResult.GetException());
            }

            return ResultBox.FromValue(cosmosEvent.ToEvent(payloadResult.GetValue()));
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return ResultBox.Error<Event>(new KeyNotFoundException($"Event with ID {eventId} not found"));
        }
        catch (CosmosException ex)
        {
            return ResultBox.Error<Event>(ex);
        }
        catch (JsonException ex)
        {
            return ResultBox.Error<Event>(ex);
        }
        catch (FormatException ex)
        {
            return ResultBox.Error<Event>(ex);
        }
        catch (InvalidOperationException ex)
        {
            return ResultBox.Error<Event>(ex);
        }
        catch (ArgumentException ex)
        {
            return ResultBox.Error<Event>(ex);
        }
    }

    /// <summary>
    ///     Writes events and associated tags.
    /// </summary>
    public async Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
        WriteEventsAsync(IEnumerable<Event> events)
    {
        try
        {
            var eventsContainer = await _context.GetEventsContainerAsync().ConfigureAwait(false);
            var tagsContainer = await _context.GetTagsContainerAsync().ConfigureAwait(false);

            var eventsList = events.ToList();
            var writtenEvents = new List<Event>();
            var tagWriteResults = new List<TagWriteResult>();

            // Use transaction batch for atomicity within partition
            // Note: CosmosDB transactions are limited to single partition
            // For cross-partition, we'll use individual operations

            foreach (var ev in eventsList)
            {
                // Serialize the event payload
                var serializedPayload = SerializeEventPayload(ev.Payload);

                // Create and write the CosmosDB event
                var cosmosEvent = CosmosEvent.FromEvent(ev, serializedPayload);

                await eventsContainer.CreateItemAsync(
                    cosmosEvent,
                    new PartitionKey(cosmosEvent.Id)).ConfigureAwait(false);

                writtenEvents.Add(ev);

                // Process tags associated with this event
                foreach (var tagString in ev.Tags)
                {
                    // Extract tag group from tag string (format: "group:content")
                    var tagGroup = tagString.Contains(':', StringComparison.Ordinal) ? tagString.Split(':')[0] : tagString;

                    // Create a tag entry for this event
                    var cosmosTag = CosmosTag.FromEventTag(
                        tagString,
                        tagGroup,
                        ev.SortableUniqueIdValue,
                        ev.Id,
                        ev.EventType);

                    await tagsContainer.CreateItemAsync(
                        cosmosTag,
                        new PartitionKey(cosmosTag.Tag)).ConfigureAwait(false);

                    tagWriteResults.Add(
                        new TagWriteResult(
                            tagString,
                            1, // Version placeholder
                            DateTimeOffset.UtcNow));
                }
            }

            return ResultBox.FromValue(
                (Events: (IReadOnlyList<Event>)writtenEvents,
                    TagWrites: (IReadOnlyList<TagWriteResult>)tagWriteResults));
        }
        catch (CosmosException ex)
        {
            Console.WriteLine($"WriteEventsAsync failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(ex);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"WriteEventsAsync failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(ex);
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"WriteEventsAsync failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(ex);
        }
    }

    /// <summary>
    ///     Reads tag stream entries for a tag.
    /// </summary>
    public async Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
    {
        try
        {
            var tagString = tag.GetTag();
            var tagsContainer = await _context.GetTagsContainerAsync().ConfigureAwait(false);

            var query = tagsContainer.GetItemLinqQueryable<CosmosTag>()
                .Where(t => t.Tag == tagString)
                .OrderBy(t => t.SortableUniqueId);

            var tagStreams = new List<TagStream>();
            using var iterator = query.ToFeedIterator();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync().ConfigureAwait(false);
                foreach (var cosmosTag in response)
                {
                    tagStreams.Add(new TagStream(
                        cosmosTag.Tag,
                        Guid.Parse(cosmosTag.EventId),
                        cosmosTag.SortableUniqueId));
                }
            }

            return ResultBox.FromValue<IEnumerable<TagStream>>(tagStreams);
        }
        catch (CosmosException ex)
        {
            return ResultBox.Error<IEnumerable<TagStream>>(ex);
        }
        catch (FormatException ex)
        {
            return ResultBox.Error<IEnumerable<TagStream>>(ex);
        }
        catch (InvalidOperationException ex)
        {
            return ResultBox.Error<IEnumerable<TagStream>>(ex);
        }
        catch (ArgumentException ex)
        {
            return ResultBox.Error<IEnumerable<TagStream>>(ex);
        }
    }

    /// <summary>
    ///     Gets the latest tag state placeholder for a tag.
    /// </summary>
    public async Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(tag);
            var tagString = tag.GetTag();
            var tagGroup = tag.GetTagGroup();
            var tagContent = tag.GetTagContent();

            var tagsContainer = await _context.GetTagsContainerAsync().ConfigureAwait(false);

            // Get the latest tag entry
            var query = tagsContainer.GetItemLinqQueryable<CosmosTag>()
                .Where(t => t.Tag == tagString)
                .OrderByDescending(t => t.SortableUniqueId)
                .Take(1);

            CosmosTag? latestTag = null;
            using var iterator = query.ToFeedIterator();

            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync().ConfigureAwait(false);
                latestTag = response.FirstOrDefault();
            }

            if (latestTag == null)
            {
                // Return empty tag state
                return ResultBox.FromValue(
                    new TagState(
                        new EmptyTagStatePayload(),
                        0,
                        string.Empty,
                        tagGroup,
                        tagContent,
                        string.Empty,
                        string.Empty));
            }

            // Return a simple tag state with just the latest sortable unique ID
            // The actual state would be computed by projectors
            return ResultBox.FromValue(
                new TagState(
                    new EmptyTagStatePayload(),
                    0,
                    latestTag.SortableUniqueId,
                    tagGroup,
                    tagContent,
                    string.Empty,
                    string.Empty));
        }
        catch (CosmosException ex)
        {
            return ResultBox.Error<TagState>(ex);
        }
        catch (InvalidOperationException ex)
        {
            return ResultBox.Error<TagState>(ex);
        }
        catch (ArgumentException ex)
        {
            return ResultBox.Error<TagState>(ex);
        }
    }

    /// <summary>
    ///     Checks whether any entries exist for a tag.
    /// </summary>
    public async Task<ResultBox<bool>> TagExistsAsync(ITag tag)
    {
        try
        {
            var tagString = tag.GetTag();
            var tagsContainer = await _context.GetTagsContainerAsync().ConfigureAwait(false);

            var query = tagsContainer.GetItemLinqQueryable<CosmosTag>()
                .Where(t => t.Tag == tagString)
                .Take(1);

            using var iterator = query.ToFeedIterator();

            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync().ConfigureAwait(false);
                return ResultBox.FromValue(response.Count > 0);
            }

            return ResultBox.FromValue(false);
        }
        catch (CosmosException ex)
        {
            return ResultBox.Error<bool>(ex);
        }
        catch (InvalidOperationException ex)
        {
            return ResultBox.Error<bool>(ex);
        }
        catch (ArgumentException ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    private string SerializeEventPayload(IEventPayload payload) =>
        _domainTypes.EventTypes.SerializeEventPayload(payload);

    private ResultBox<IEventPayload> DeserializeEventPayload(string eventType, string json)
    {
        try
        {
            var payload = _domainTypes.EventTypes.DeserializeEventPayload(eventType, json);
            if (payload == null)
            {
                return ResultBox.Error<IEventPayload>(
                    new InvalidOperationException(
                        $"Failed to deserialize event payload of type {eventType}. Make sure the event type is registered."));
            }

            return ResultBox.FromValue(payload);
        }
        catch (JsonException ex)
        {
            return ResultBox.Error<IEventPayload>(ex);
        }
        catch (InvalidOperationException ex)
        {
            return ResultBox.Error<IEventPayload>(ex);
        }
        catch (ArgumentException ex)
        {
            return ResultBox.Error<IEventPayload>(ex);
        }
    }
}
