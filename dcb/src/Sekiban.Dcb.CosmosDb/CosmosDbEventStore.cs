using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
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
    private readonly ILogger<CosmosDbEventStore>? _logger;

    /// <summary>
    ///     Creates a new CosmosDB event store.
    /// </summary>
    public CosmosDbEventStore(CosmosDbContext context, DcbDomainTypes domainTypes, ILogger<CosmosDbEventStore>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _logger = logger;
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
    ///     Events are written in parallel (distributed partition keys).
    ///     Tags are written using TransactionalBatch (same partition key per tag).
    /// </summary>
    public async Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
        WriteEventsAsync(IEnumerable<Event> events)
    {
        var options = _context.Options;

        try
        {
            var eventsContainer = await _context.GetEventsContainerAsync().ConfigureAwait(false);
            var tagsContainer = await _context.GetTagsContainerAsync().ConfigureAwait(false);

            var eventsList = events.ToList();
            if (eventsList.Count == 0)
            {
                return ResultBox.FromValue(
                    (Events: (IReadOnlyList<Event>)Array.Empty<Event>(),
                        TagWrites: (IReadOnlyList<TagWriteResult>)Array.Empty<TagWriteResult>()));
            }

            // Step 1: Write events in parallel with concurrency control
            // Events have distributed partition keys (PK = eventId), so parallel writes are efficient
            var writtenEvents = await WriteEventsInParallelAsync(
                eventsList,
                eventsContainer,
                options).ConfigureAwait(false);

            // Step 2: Write tags using TransactionalBatch (grouped by tag = same partition key)
            List<TagWriteResult> tagWriteResults;
            try
            {
                tagWriteResults = await WriteTagsWithBatchAsync(
                    writtenEvents,
                    tagsContainer,
                    options).ConfigureAwait(false);
            }
            catch (Exception tagEx)
            {
                // Tag write failed - attempt to rollback written events
                if (options.TryRollbackOnFailure)
                {
                    await TryRollbackEventsAsync(writtenEvents, eventsContainer).ConfigureAwait(false);
                }

                throw new InvalidOperationException(
                    $"Tag write failed after {writtenEvents.Count} events were written. " +
                    $"Rollback attempted: {options.TryRollbackOnFailure}",
                    tagEx);
            }

            return ResultBox.FromValue(
                (Events: (IReadOnlyList<Event>)writtenEvents,
                    TagWrites: (IReadOnlyList<TagWriteResult>)tagWriteResults));
        }
        catch (CosmosException ex)
        {
            _logger?.LogError(ex, "WriteEventsAsync failed with CosmosException: {Message}", ex.Message);
            return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(ex);
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "WriteEventsAsync failed: {Message}", ex.Message);
            return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(ex);
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "WriteEventsAsync failed: {Message}", ex.Message);
            return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(ex);
        }
    }

    /// <summary>
    ///     Writes events in parallel with concurrency control.
    /// </summary>
    private async Task<List<Event>> WriteEventsInParallelAsync(
        List<Event> eventsList,
        Container eventsContainer,
        CosmosDbEventStoreOptions options)
    {
        using var semaphore = new SemaphoreSlim(options.MaxConcurrentEventWrites);
        var itemRequestOptions = new ItemRequestOptions
        {
            EnableContentResponseOnWrite = options.EnableContentResponseOnWrite
        };

        var eventTasks = eventsList.Select(async ev =>
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var serializedPayload = SerializeEventPayload(ev.Payload);
                var cosmosEvent = CosmosEvent.FromEvent(ev, serializedPayload);

                await eventsContainer.CreateItemAsync(
                    cosmosEvent,
                    new PartitionKey(cosmosEvent.Id),
                    itemRequestOptions).ConfigureAwait(false);

                return ev;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(eventTasks).ConfigureAwait(false);
        return results.ToList();
    }

    /// <summary>
    ///     Writes tags using TransactionalBatch for efficiency.
    ///     Tags with the same partition key (tag string) are batched together.
    /// </summary>
    private async Task<List<TagWriteResult>> WriteTagsWithBatchAsync(
        List<Event> writtenEvents,
        Container tagsContainer,
        CosmosDbEventStoreOptions options)
    {
        var tagWriteResults = new List<TagWriteResult>();

        // Group all tag entries by tag string (partition key)
        var tagEntries = writtenEvents
            .SelectMany(ev => ev.Tags.Select(tagString => (TagString: tagString, Event: ev)))
            .GroupBy(x => x.TagString);

        foreach (var group in tagEntries)
        {
            var tagString = group.Key;
            var tagGroup = tagString.Contains(':', StringComparison.Ordinal)
                ? tagString.Split(':')[0]
                : tagString;
            var items = group.ToList();

            if (options.UseTransactionalBatchForTags && items.Count <= options.MaxBatchOperations)
            {
                // Use TransactionalBatch for same-partition writes
                var batch = tagsContainer.CreateTransactionalBatch(new PartitionKey(tagString));

                foreach (var item in items)
                {
                    var cosmosTag = CosmosTag.FromEventTag(
                        tagString,
                        tagGroup,
                        item.Event.SortableUniqueIdValue,
                        item.Event.Id,
                        item.Event.EventType);

                    batch.CreateItem(cosmosTag);
                }

                var response = await batch.ExecuteAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw new CosmosException(
                        $"TransactionalBatch failed for tag '{tagString}' with status {response.StatusCode}",
                        response.StatusCode,
                        (int)response.StatusCode,
                        response.ActivityId,
                        response.RequestCharge);
                }

                // Add results for all items in batch
                foreach (var item in items)
                {
                    tagWriteResults.Add(new TagWriteResult(tagString, 1, DateTimeOffset.UtcNow));
                }
            }
            else
            {
                // Fallback: Split into multiple batches if exceeding limit
                for (var i = 0; i < items.Count; i += options.MaxBatchOperations)
                {
                    var batchItems = items.Skip(i).Take(options.MaxBatchOperations).ToList();
                    var batch = tagsContainer.CreateTransactionalBatch(new PartitionKey(tagString));

                    foreach (var item in batchItems)
                    {
                        var cosmosTag = CosmosTag.FromEventTag(
                            tagString,
                            tagGroup,
                            item.Event.SortableUniqueIdValue,
                            item.Event.Id,
                            item.Event.EventType);

                        batch.CreateItem(cosmosTag);
                    }

                    var response = await batch.ExecuteAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new CosmosException(
                            $"TransactionalBatch failed for tag '{tagString}' (batch {i / options.MaxBatchOperations + 1}) with status {response.StatusCode}",
                            response.StatusCode,
                            (int)response.StatusCode,
                            response.ActivityId,
                            response.RequestCharge);
                    }

                    foreach (var item in batchItems)
                    {
                        tagWriteResults.Add(new TagWriteResult(tagString, 1, DateTimeOffset.UtcNow));
                    }
                }
            }
        }

        return tagWriteResults;
    }

    /// <summary>
    ///     Attempts to rollback (delete) written events after a failure.
    /// </summary>
    private async Task TryRollbackEventsAsync(List<Event> writtenEvents, Container eventsContainer)
    {
        var deleteTasks = writtenEvents.Select(async ev =>
        {
            try
            {
                await eventsContainer.DeleteItemAsync<CosmosEvent>(
                    ev.Id.ToString(),
                    new PartitionKey(ev.Id.ToString())).ConfigureAwait(false);
                return (ev.Id, Success: true, Error: (Exception?)null);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Already deleted or never written - consider success
                return (ev.Id, Success: true, Error: null);
            }
            catch (Exception ex)
            {
                return (ev.Id, Success: false, Error: ex);
            }
        });

        var results = await Task.WhenAll(deleteTasks).ConfigureAwait(false);
        var failedDeletes = results.Where(r => !r.Success).ToList();

        if (failedDeletes.Count > 0)
        {
            var orphanedIds = string.Join(", ", failedDeletes.Select(f => f.Id));
            _logger?.LogError(
                "Failed to rollback {FailedCount} events after tag write failure. Orphaned event IDs: {OrphanedIds}",
                failedDeletes.Count,
                orphanedIds);
        }
        else
        {
            _logger?.LogInformation(
                "Successfully rolled back {Count} events after tag write failure",
                writtenEvents.Count);
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

    /// <inheritdoc />
    public async Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null)
    {
        try
        {
            var container = await _context.GetEventsContainerAsync().ConfigureAwait(false);

            IQueryable<CosmosEvent> query = container.GetItemLinqQueryable<CosmosEvent>();
            if (since != null)
            {
                query = query.Where(e => e.SortableUniqueId.CompareTo(since.Value) > 0);
            }

            long count = 0;
            using var iterator = query.ToFeedIterator();
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync().ConfigureAwait(false);
                count += response.Count;
            }

            return ResultBox.FromValue(count);
        }
        catch (CosmosException ex)
        {
            return ResultBox.Error<long>(ex);
        }
        catch (InvalidOperationException ex)
        {
            return ResultBox.Error<long>(ex);
        }
        catch (ArgumentException ex)
        {
            return ResultBox.Error<long>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null)
    {
        try
        {
            var tagsContainer = await _context.GetTagsContainerAsync().ConfigureAwait(false);

            // Query all tags and group in memory (Cosmos DB doesn't support complex GROUP BY with aggregations)
            IQueryable<CosmosTag> query = tagsContainer.GetItemLinqQueryable<CosmosTag>();
            if (!string.IsNullOrEmpty(tagGroup))
            {
                query = query.Where(t => t.TagGroup == tagGroup);
            }

            var allTags = new List<CosmosTag>();
            using var iterator = query.ToFeedIterator();
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync().ConfigureAwait(false);
                allTags.AddRange(response);
            }

            // Group in memory
            var tagInfos = allTags
                .GroupBy(t => new { t.Tag, t.TagGroup })
                .Select(g => new TagInfo(
                    g.Key.Tag,
                    g.Key.TagGroup,
                    g.Count(),
                    g.Min(t => t.SortableUniqueId),
                    g.Max(t => t.SortableUniqueId),
                    g.Min(t => t.CreatedAt),
                    g.Max(t => t.CreatedAt)))
                .OrderBy(t => t.TagGroup)
                .ThenBy(t => t.Tag)
                .ToList();

            return ResultBox.FromValue(tagInfos.AsEnumerable());
        }
        catch (CosmosException ex)
        {
            return ResultBox.Error<IEnumerable<TagInfo>>(ex);
        }
        catch (InvalidOperationException ex)
        {
            return ResultBox.Error<IEnumerable<TagInfo>>(ex);
        }
        catch (ArgumentException ex)
        {
            return ResultBox.Error<IEnumerable<TagInfo>>(ex);
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
