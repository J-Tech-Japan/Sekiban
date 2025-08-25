using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Net;
namespace Sekiban.Dcb.CosmosDb;

public class CosmosDbEventStore : IEventStore
{
    private readonly CosmosDbContext _context;
    private readonly DcbDomainTypes _domainTypes;

    public CosmosDbEventStore(CosmosDbContext context, DcbDomainTypes domainTypes)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
    }

    public async Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null)
    {
        try
        {
            var container = await _context.GetEventsContainerAsync();

            var query = container.GetItemLinqQueryable<CosmosEvent>()
                .OrderBy(e => e.SortableUniqueId);

            if (since != null)
            {
                query = query.Where(e => string.Compare(e.SortableUniqueId, since.Value) > 0)
                    .OrderBy(e => e.SortableUniqueId);
            }

            var events = new List<Event>();
            using var iterator = query.ToFeedIterator();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
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
        catch (Exception ex)
        {
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
    }

    public async Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        try
        {
            var tagString = tag.GetTag();

            // First, get all event IDs for this tag from the tags container
            var tagsContainer = await _context.GetTagsContainerAsync();
            var tagQuery = tagsContainer.GetItemLinqQueryable<CosmosTag>()
                .Where(t => t.Tag == tagString);

            if (since != null)
            {
                tagQuery = tagQuery.Where(t => string.Compare(t.SortableUniqueId, since.Value) > 0);
            }

            var eventIds = new List<string>();
            using (var tagIterator = tagQuery.OrderBy(t => t.SortableUniqueId).ToFeedIterator())
            {
                while (tagIterator.HasMoreResults)
                {
                    var response = await tagIterator.ReadNextAsync();
                    eventIds.AddRange(response.Select(t => t.EventId));
                }
            }

            if (!eventIds.Any())
            {
                return ResultBox.FromValue<IEnumerable<Event>>(new List<Event>());
            }

            // Now fetch the events by their IDs
            var eventsContainer = await _context.GetEventsContainerAsync();
            var events = new List<Event>();

            // Batch read events for better performance
            var tasks = eventIds.Select(async eventId =>
            {
                try
                {
                    var response = await eventsContainer.ReadItemAsync<CosmosEvent>(
                        eventId,
                        new PartitionKey(eventId));

                    return response.Resource;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // Event might have been deleted, skip it
                    return null;
                }
            });

            var cosmosEvents = await Task.WhenAll(tasks);

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
        catch (Exception ex)
        {
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
    }

    public async Task<ResultBox<Event>> ReadEventAsync(Guid eventId)
    {
        try
        {
            var container = await _context.GetEventsContainerAsync();
            var eventIdStr = eventId.ToString();

            var response = await container.ReadItemAsync<CosmosEvent>(
                eventIdStr,
                new PartitionKey(eventIdStr));

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
            return ResultBox.Error<Event>(new Exception($"Event with ID {eventId} not found"));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<Event>(ex);
        }
    }

    public async Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
        WriteEventsAsync(IEnumerable<Event> events)
    {
        try
        {
            var eventsContainer = await _context.GetEventsContainerAsync();
            var tagsContainer = await _context.GetTagsContainerAsync();

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
                    new PartitionKey(cosmosEvent.Id));

                writtenEvents.Add(ev);

                // Process tags associated with this event
                foreach (var tagString in ev.Tags)
                {
                    // Extract tag group from tag string (format: "group:content")
                    var tagGroup = tagString.Contains(':') ? tagString.Split(':')[0] : tagString;

                    // Create a tag entry for this event
                    var cosmosTag = CosmosTag.FromEventTag(
                        tagString,
                        tagGroup,
                        ev.SortableUniqueIdValue,
                        ev.Id,
                        ev.EventType);

                    await tagsContainer.CreateItemAsync(
                        cosmosTag,
                        new PartitionKey(cosmosTag.Tag));

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
        catch (Exception ex)
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

    public async Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
    {
        try
        {
            var tagString = tag.GetTag();
            var tagsContainer = await _context.GetTagsContainerAsync();

            var query = tagsContainer.GetItemLinqQueryable<CosmosTag>()
                .Where(t => t.Tag == tagString)
                .OrderBy(t => t.SortableUniqueId);

            var tagStreams = new List<TagStream>();
            using var iterator = query.ToFeedIterator();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
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
        catch (Exception ex)
        {
            return ResultBox.Error<IEnumerable<TagStream>>(ex);
        }
    }

    public async Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
    {
        try
        {
            var tagString = tag.GetTag();
            var tagGroup = tag.GetTagGroup();
            var tagContent = tagString.Substring(tagGroup.Length + 1);

            var tagsContainer = await _context.GetTagsContainerAsync();

            // Get the latest tag entry
            var query = tagsContainer.GetItemLinqQueryable<CosmosTag>()
                .Where(t => t.Tag == tagString)
                .OrderByDescending(t => t.SortableUniqueId)
                .Take(1);

            CosmosTag? latestTag = null;
            using var iterator = query.ToFeedIterator();

            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
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
        catch (Exception ex)
        {
            return ResultBox.Error<TagState>(ex);
        }
    }

    public async Task<ResultBox<bool>> TagExistsAsync(ITag tag)
    {
        try
        {
            var tagString = tag.GetTag();
            var tagsContainer = await _context.GetTagsContainerAsync();

            var query = tagsContainer.GetItemLinqQueryable<CosmosTag>()
                .Where(t => t.Tag == tagString)
                .Take(1);

            using var iterator = query.ToFeedIterator();

            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                return ResultBox.FromValue(response.Any());
            }

            return ResultBox.FromValue(false);
        }
        catch (Exception ex)
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
                    new Exception(
                        $"Failed to deserialize event payload of type {eventType}. Make sure the event type is registered."));
            }

            return ResultBox.FromValue(payload);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IEventPayload>(ex);
        }
    }
}