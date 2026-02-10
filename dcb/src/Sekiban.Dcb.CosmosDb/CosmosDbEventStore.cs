using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Net;
using System.Text.Json;
namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     CosmosDB-backed event store implementation.
/// </summary>
public partial class CosmosDbEventStore : IEventStore
{
    private const string ParamSince = "@since";
    private const string ParamServiceId = "@serviceId";
    private readonly CosmosDbContext _context;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly ICosmosContainerResolver _containerResolver;
    private readonly ILogger<CosmosDbEventStore>? _logger;

    /// <summary>
    ///     Creates a new CosmosDB event store.
    /// </summary>
    public CosmosDbEventStore(
        CosmosDbContext context,
        DcbDomainTypes domainTypes,
        IServiceIdProvider serviceIdProvider,
        ICosmosContainerResolver containerResolver,
        ILogger<CosmosDbEventStore>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _serviceIdProvider = serviceIdProvider ?? throw new ArgumentNullException(nameof(serviceIdProvider));
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _logger = logger;
    }

    private string CurrentServiceId => _serviceIdProvider.GetCurrentServiceId();

    private static string GetEventPartitionKey(string eventId, CosmosContainerSettings settings, string serviceId) =>
        settings.IsLegacy ? eventId : $"{serviceId}|{eventId}";

    private static string GetTagPartitionKey(string tag, CosmosContainerSettings settings, string serviceId) =>
        settings.IsLegacy ? tag : $"{serviceId}|{tag}";

    private static bool ServiceIdMatches(string? storedServiceId, string currentServiceId, bool allowEmpty) =>
        allowEmpty
            ? string.IsNullOrEmpty(storedServiceId) || string.Equals(storedServiceId, currentServiceId, StringComparison.Ordinal)
            : string.Equals(storedServiceId, currentServiceId, StringComparison.Ordinal);

    /// <summary>
    ///     Reads all events, optionally after a given sortable unique ID.
    ///     Optimized for high-throughput bulk reads with parallel deserialization.
    /// </summary>
    public async Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null)
    {
        try
        {
            var serviceId = CurrentServiceId;
            var settings = _containerResolver.ResolveEventsContainer(serviceId);
            var container = await _context.GetEventsContainerAsync(settings).ConfigureAwait(false);
            var options = _context.Options;

            QueryDefinition queryDefinition;
            if (settings.IsLegacy)
            {
                queryDefinition = since != null
                    ? new QueryDefinition($"SELECT * FROM c WHERE c.sortableUniqueId > {ParamSince} ORDER BY c.sortableUniqueId")
                        .WithParameter(ParamSince, since.Value)
                    : new QueryDefinition("SELECT * FROM c ORDER BY c.sortableUniqueId");
            }
            else
            {
                queryDefinition = since != null
                    ? new QueryDefinition($"SELECT * FROM c WHERE c.serviceId = {ParamServiceId} AND c.sortableUniqueId > {ParamSince} ORDER BY c.sortableUniqueId")
                        .WithParameter(ParamServiceId, serviceId)
                        .WithParameter(ParamSince, since.Value)
                    : new QueryDefinition($"SELECT * FROM c WHERE c.serviceId = {ParamServiceId} ORDER BY c.sortableUniqueId")
                        .WithParameter(ParamServiceId, serviceId);
            }
            var queryRequestOptions = options.CreateOptimizedQueryRequestOptions();

            var events = new List<Event>();
            var totalRuConsumed = 0.0;
            var totalEventsRead = 0;

            using var iterator = container.GetItemQueryIterator<CosmosEvent>(queryDefinition, requestOptions: queryRequestOptions);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync().ConfigureAwait(false);
                totalRuConsumed += response.RequestCharge;

                // Process page with parallel deserialization
                var pageEvents = await ProcessPageWithParallelDeserializationAsync(
                    response.ToList(),
                    options.MaxConcurrentDeserializations).ConfigureAwait(false);

                if (!pageEvents.IsSuccess)
                {
                    return ResultBox.Error<IEnumerable<Event>>(pageEvents.GetException());
                }

                events.AddRange(pageEvents.GetValue());
                totalEventsRead += response.Count;

                // Report progress if callback is configured
                options.ReadProgressCallback?.Invoke(totalEventsRead, totalRuConsumed);
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
    ///     Processes a page of CosmosEvents with parallel deserialization.
    /// </summary>
    private async Task<ResultBox<List<Event>>> ProcessPageWithParallelDeserializationAsync(
        List<CosmosEvent> cosmosEvents,
        int maxConcurrency)
    {
        if (cosmosEvents.Count == 0)
        {
            return ResultBox.FromValue(new List<Event>());
        }

        // For small pages, sequential is faster due to task overhead
        if (cosmosEvents.Count <= 50)
        {
            var sequentialEvents = new List<Event>(cosmosEvents.Count);
            foreach (var cosmosEvent in cosmosEvents)
            {
                var payloadResult = DeserializeEventPayload(cosmosEvent.EventType, cosmosEvent.Payload);
                if (!payloadResult.IsSuccess)
                {
                    return ResultBox.Error<List<Event>>(payloadResult.GetException());
                }
                sequentialEvents.Add(cosmosEvent.ToEvent(payloadResult.GetValue()));
            }
            return ResultBox.FromValue(sequentialEvents);
        }

        // Parallel deserialization for larger pages
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = cosmosEvents.Select(async cosmosEvent =>
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var payloadResult = DeserializeEventPayload(cosmosEvent.EventType, cosmosEvent.Payload);
                if (!payloadResult.IsSuccess)
                {
                    return (Event: (Event?)null, Error: payloadResult.GetException());
                }
                return (Event: cosmosEvent.ToEvent(payloadResult.GetValue()), Error: (Exception?)null);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Check for any errors
        var firstError = results.FirstOrDefault(r => r.Error != null);
        if (firstError.Error != null)
        {
            return ResultBox.Error<List<Event>>(firstError.Error);
        }

        // Collect successful results preserving order
        var events = results.Select(r => r.Event!).ToList();
        return ResultBox.FromValue(events);
    }

    /// <summary>
    ///     Reads events by tag, optionally after a given sortable unique ID.
    ///     Optimized with batched point reads and parallel deserialization.
    /// </summary>
    public async Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        ArgumentNullException.ThrowIfNull(tag);
        try
        {
            var tagString = tag.GetTag();
            var serviceId = CurrentServiceId;
            var options = _context.Options;

            // First, get all event IDs for this tag from the tags container using optimized query
            var tagsSettings = _containerResolver.ResolveTagsContainer(serviceId);
            var tagsContainer = await _context.GetTagsContainerAsync(tagsSettings).ConfigureAwait(false);

            var (queryDefinition, requestOptions) = BuildTagEventIdQuery(tagString, serviceId, tagsSettings, since);
            var eventIds = await FetchEventIdsAsync(tagsContainer, queryDefinition, requestOptions).ConfigureAwait(false);

            if (eventIds.Count == 0)
            {
                return ResultBox.FromValue<IEnumerable<Event>>(new List<Event>());
            }

            // Fetch events using batched point reads with concurrency control
            var eventsSettings = _containerResolver.ResolveEventsContainer(serviceId);
            var eventsContainer = await _context.GetEventsContainerAsync(eventsSettings).ConfigureAwait(false);
            return await FetchEventsByIdsAsync(
                eventsContainer,
                eventsSettings,
                serviceId,
                eventIds,
                options.MaxConcurrentDeserializations).ConfigureAwait(false);
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

    private static (QueryDefinition QueryDefinition, QueryRequestOptions? RequestOptions) BuildTagEventIdQuery(
        string tagString,
        string serviceId,
        CosmosContainerSettings tagsSettings,
        SortableUniqueId? since)
    {
        if (tagsSettings.IsLegacy)
        {
            var queryDefinition = since != null
                ? new QueryDefinition($"SELECT c.eventId FROM c WHERE c.tag = @tag AND c.sortableUniqueId > {ParamSince} ORDER BY c.sortableUniqueId")
                    .WithParameter("@tag", tagString)
                    .WithParameter(ParamSince, since.Value)
                : new QueryDefinition("SELECT c.eventId FROM c WHERE c.tag = @tag ORDER BY c.sortableUniqueId")
                    .WithParameter("@tag", tagString);

            var requestOptions = new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(tagString)
            };

            return (queryDefinition, requestOptions);
        }

        var tagPk = GetTagPartitionKey(tagString, tagsSettings, serviceId);
        var queryDefinitionV2 = since != null
            ? new QueryDefinition($"SELECT c.eventId FROM c WHERE c.pk = @pk AND c.sortableUniqueId > {ParamSince} ORDER BY c.sortableUniqueId")
                .WithParameter("@pk", tagPk)
                .WithParameter(ParamSince, since.Value)
            : new QueryDefinition("SELECT c.eventId FROM c WHERE c.pk = @pk ORDER BY c.sortableUniqueId")
                .WithParameter("@pk", tagPk);

        var requestOptionsV2 = new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(tagPk)
        };

        return (queryDefinitionV2, requestOptionsV2);
    }

    private static async Task<List<string>> FetchEventIdsAsync(
        Container tagsContainer,
        QueryDefinition queryDefinition,
        QueryRequestOptions? requestOptions)
    {
        var eventIds = new List<string>();
        using var tagIterator = tagsContainer.GetItemQueryIterator<dynamic>(queryDefinition, requestOptions: requestOptions);
        while (tagIterator.HasMoreResults)
        {
            var response = await tagIterator.ReadNextAsync().ConfigureAwait(false);
            foreach (var item in response)
            {
                eventIds.Add((string)item.eventId);
            }
        }

        return eventIds;
    }

    private async Task<ResultBox<IEnumerable<Event>>> FetchEventsByIdsAsync(
        Container eventsContainer,
        CosmosContainerSettings eventsSettings,
        string serviceId,
        IReadOnlyList<string> eventIds,
        int maxConcurrency)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency);

        var tasks = eventIds.Select(async eventId =>
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var eventPk = GetEventPartitionKey(eventId, eventsSettings, serviceId);
                var response = await eventsContainer.ReadItemAsync<CosmosEvent>(
                    eventId,
                    new PartitionKey(eventPk)).ConfigureAwait(false);

                var cosmosEvent = response.Resource;
                if (!ServiceIdMatches(cosmosEvent.ServiceId, serviceId, allowEmpty: eventsSettings.IsLegacy))
                {
                    return (Event: (Event?)null, Error: new UnauthorizedAccessException(
                        $"Event {eventId} does not belong to service {serviceId}"), SortKey: string.Empty);
                }
                var payloadResult = DeserializeEventPayload(cosmosEvent.EventType, cosmosEvent.Payload);
                if (!payloadResult.IsSuccess)
                {
                    return (Event: (Event?)null, Error: payloadResult.GetException(), SortKey: string.Empty);
                }

                var ev = cosmosEvent.ToEvent(payloadResult.GetValue());
                return (Event: ev, Error: (Exception?)null, SortKey: ev.SortableUniqueIdValue);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Event might have been deleted, skip it
                return (Event: null, Error: null, SortKey: string.Empty);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var firstError = results.FirstOrDefault(r => r.Error != null);
        if (firstError.Error != null)
        {
            return ResultBox.Error<IEnumerable<Event>>(firstError.Error);
        }

        var events = results
            .Where(r => r.Event != null)
            .OrderBy(r => r.SortKey)
            .Select(r => r.Event!)
            .ToList();

        return ResultBox.FromValue<IEnumerable<Event>>(events);
    }

    /// <summary>
    ///     Reads a single event by ID.
    /// </summary>
    public async Task<ResultBox<Event>> ReadEventAsync(Guid eventId)
    {
        try
        {
            var eventIdStr = eventId.ToString();
            var serviceId = CurrentServiceId;
            var settings = _containerResolver.ResolveEventsContainer(serviceId);
            var container = await _context.GetEventsContainerAsync(settings).ConfigureAwait(false);
            var pk = GetEventPartitionKey(eventIdStr, settings, serviceId);

            var response = await container.ReadItemAsync<CosmosEvent>(
                eventIdStr,
                new PartitionKey(pk)).ConfigureAwait(false);

            var cosmosEvent = response.Resource;
            if (!ServiceIdMatches(cosmosEvent.ServiceId, serviceId, allowEmpty: settings.IsLegacy))
            {
                return ResultBox.Error<Event>(
                    new UnauthorizedAccessException($"Event {eventId} does not belong to service {serviceId}"));
            }

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
        var serviceId = CurrentServiceId;

        try
        {
            var eventsSettings = _containerResolver.ResolveEventsContainer(serviceId);
            var tagsSettings = _containerResolver.ResolveTagsContainer(serviceId);
            var eventsContainer = await _context.GetEventsContainerAsync(eventsSettings).ConfigureAwait(false);
            var tagsContainer = await _context.GetTagsContainerAsync(tagsSettings).ConfigureAwait(false);

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
                options,
                serviceId,
                eventsSettings).ConfigureAwait(false);

            // Step 2: Write tags using TransactionalBatch (grouped by tag = same partition key)
            List<TagWriteResult> tagWriteResults;
            try
            {
                tagWriteResults = await WriteTagsWithBatchAsync(
                    writtenEvents,
                    tagsContainer,
                    options,
                    serviceId,
                    tagsSettings).ConfigureAwait(false);
            }
            catch (Exception tagEx)
            {
                // Tag write failed - attempt to rollback written events
                if (options.TryRollbackOnFailure)
                {
                    await TryRollbackEventsAsync(writtenEvents, eventsContainer, serviceId, eventsSettings).ConfigureAwait(false);
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
            if (_logger != null)
                LogWriteEventsCosmosError(_logger, ex, ex.Message);
            return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(ex);
        }
        catch (InvalidOperationException ex)
        {
            if (_logger != null)
                LogWriteEventsFailed(_logger, ex, ex.Message);
            return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(ex);
        }
        catch (ArgumentException ex)
        {
            if (_logger != null)
                LogWriteEventsFailed(_logger, ex, ex.Message);
            return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(ex);
        }
    }

    /// <summary>
    ///     Writes events in parallel with concurrency control.
    /// </summary>
    private async Task<List<Event>> WriteEventsInParallelAsync(
        List<Event> eventsList,
        Container eventsContainer,
        CosmosDbEventStoreOptions options,
        string serviceId,
        CosmosContainerSettings settings)
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
                var cosmosEvent = CosmosEvent.FromEvent(ev, serializedPayload, serviceId);
                var eventPk = GetEventPartitionKey(cosmosEvent.Id, settings, serviceId);

                await eventsContainer.CreateItemAsync(
                    cosmosEvent,
                    new PartitionKey(eventPk),
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
    private static async Task<List<TagWriteResult>> WriteTagsWithBatchAsync(
        List<Event> writtenEvents,
        Container tagsContainer,
        CosmosDbEventStoreOptions options,
        string serviceId,
        CosmosContainerSettings settings)
    {
        var tagWriteResults = new List<TagWriteResult>();

        // Group all tag entries by tag string (partition key)
        var tagEntries = writtenEvents
            .SelectMany(ev => ev.Tags.Select(tagString => (TagString: tagString, Event: ev)))
            .GroupBy(x => settings.IsLegacy ? x.TagString : $"{serviceId}|{x.TagString}");

        foreach (var group in tagEntries)
        {
            var tagPk = group.Key;
            var tagString = settings.IsLegacy ? group.Key : group.First().TagString;
            var tagGroup = tagString.Contains(':', StringComparison.Ordinal)
                ? tagString.Split(':')[0]
                : tagString;
            var items = group.ToList();

            if (options.UseTransactionalBatchForTags && items.Count <= options.MaxBatchOperations)
            {
                // Use TransactionalBatch for same-partition writes
                var batch = tagsContainer.CreateTransactionalBatch(new PartitionKey(tagPk));

                foreach (var item in items)
                {
                    var cosmosTag = CosmosTag.FromEventTag(
                        tagString,
                        tagGroup,
                        item.Event.SortableUniqueIdValue,
                        item.Event.Id,
                        item.Event.EventType,
                        serviceId);

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
                    var batch = tagsContainer.CreateTransactionalBatch(new PartitionKey(tagPk));

                    foreach (var item in batchItems)
                    {
                        var cosmosTag = CosmosTag.FromEventTag(
                            tagString,
                            tagGroup,
                            item.Event.SortableUniqueIdValue,
                            item.Event.Id,
                            item.Event.EventType,
                            serviceId);

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
    private async Task TryRollbackEventsAsync(
        List<Event> writtenEvents,
        Container eventsContainer,
        string serviceId,
        CosmosContainerSettings settings)
    {
        var deleteTasks = writtenEvents.Select(async ev =>
        {
            try
            {
                var eventId = ev.Id.ToString();
                var eventPk = GetEventPartitionKey(eventId, settings, serviceId);
                await eventsContainer.DeleteItemAsync<CosmosEvent>(
                    eventId,
                    new PartitionKey(eventPk)).ConfigureAwait(false);
                return (ev.Id, Success: true, Error: (Exception?)null);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Already deleted or never written - consider success
                return (ev.Id, Success: true, Error: null);
            }
            catch (CosmosException ex)
            {
                return (ev.Id, Success: false, Error: (Exception)ex);
            }
            catch (InvalidOperationException ex)
            {
                return (ev.Id, Success: false, Error: (Exception)ex);
            }
        });

        var results = await Task.WhenAll(deleteTasks).ConfigureAwait(false);
        var failedDeletes = results.Where(r => !r.Success).ToList();

        if (failedDeletes.Count > 0)
        {
            var orphanedIds = string.Join(", ", failedDeletes.Select(f => f.Id));
            if (_logger != null)
                LogRollbackFailed(_logger, failedDeletes.Count, orphanedIds);
        }
        else
        {
            if (_logger != null)
                LogRollbackSucceeded(_logger, writtenEvents.Count);
        }
    }

    /// <summary>
    ///     Reads tag stream entries for a tag.
    /// </summary>
    public async Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        try
        {
            var tagString = tag.GetTag();
            var serviceId = CurrentServiceId;
            var settings = _containerResolver.ResolveTagsContainer(serviceId);
            var tagsContainer = await _context.GetTagsContainerAsync(settings).ConfigureAwait(false);

            QueryDefinition queryDefinition;
            QueryRequestOptions requestOptions;
            if (settings.IsLegacy)
            {
                queryDefinition = new QueryDefinition(
                        "SELECT * FROM c WHERE c.tag = @tag ORDER BY c.sortableUniqueId")
                    .WithParameter("@tag", tagString);
                requestOptions = new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(tagString)
                };
            }
            else
            {
                var tagPk = GetTagPartitionKey(tagString, settings, serviceId);
                queryDefinition = new QueryDefinition(
                        "SELECT * FROM c WHERE c.pk = @pk ORDER BY c.sortableUniqueId")
                    .WithParameter("@pk", tagPk);
                requestOptions = new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(tagPk)
                };
            }

            var tagStreams = new List<TagStream>();
            using var iterator = tagsContainer.GetItemQueryIterator<CosmosTag>(
                queryDefinition,
                requestOptions: requestOptions);

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

            var serviceId = CurrentServiceId;
            var settings = _containerResolver.ResolveTagsContainer(serviceId);
            var tagsContainer = await _context.GetTagsContainerAsync(settings).ConfigureAwait(false);

            QueryDefinition queryDefinition;
            QueryRequestOptions requestOptions;
            if (settings.IsLegacy)
            {
                queryDefinition = new QueryDefinition(
                        "SELECT * FROM c WHERE c.tag = @tag ORDER BY c.sortableUniqueId DESC")
                    .WithParameter("@tag", tagString);
                requestOptions = new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(tagString),
                    MaxItemCount = 1
                };
            }
            else
            {
                var tagPk = GetTagPartitionKey(tagString, settings, serviceId);
                queryDefinition = new QueryDefinition(
                        "SELECT * FROM c WHERE c.pk = @pk ORDER BY c.sortableUniqueId DESC")
                    .WithParameter("@pk", tagPk);
                requestOptions = new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(tagPk),
                    MaxItemCount = 1
                };
            }

            CosmosTag? latestTag = null;
            using var iterator = tagsContainer.GetItemQueryIterator<CosmosTag>(
                queryDefinition,
                requestOptions: requestOptions);

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
        ArgumentNullException.ThrowIfNull(tag);
        try
        {
            var tagString = tag.GetTag();
            var serviceId = CurrentServiceId;
            var settings = _containerResolver.ResolveTagsContainer(serviceId);
            var tagsContainer = await _context.GetTagsContainerAsync(settings).ConfigureAwait(false);

            QueryDefinition queryDefinition;
            QueryRequestOptions requestOptions;
            if (settings.IsLegacy)
            {
                queryDefinition = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.tag = @tag")
                    .WithParameter("@tag", tagString);
                requestOptions = new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(tagString)
                };
            }
            else
            {
                var tagPk = GetTagPartitionKey(tagString, settings, serviceId);
                queryDefinition = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.pk = @pk")
                    .WithParameter("@pk", tagPk);
                requestOptions = new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(tagPk)
                };
            }

            using var iterator = tagsContainer.GetItemQueryIterator<long>(
                queryDefinition,
                requestOptions: requestOptions);

            long count = 0;
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync().ConfigureAwait(false);
                count += response.FirstOrDefault();
            }

            return ResultBox.FromValue(count > 0);
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
            var serviceId = CurrentServiceId;
            var settings = _containerResolver.ResolveEventsContainer(serviceId);
            var container = await _context.GetEventsContainerAsync(settings).ConfigureAwait(false);

            QueryDefinition queryDefinition;
            if (settings.IsLegacy)
            {
                queryDefinition = since != null
                    ? new QueryDefinition($"SELECT VALUE COUNT(1) FROM c WHERE c.sortableUniqueId > {ParamSince}")
                        .WithParameter(ParamSince, since.Value)
                    : new QueryDefinition("SELECT VALUE COUNT(1) FROM c");
            }
            else
            {
                queryDefinition = since != null
                    ? new QueryDefinition($"SELECT VALUE COUNT(1) FROM c WHERE c.serviceId = {ParamServiceId} AND c.sortableUniqueId > {ParamSince}")
                        .WithParameter(ParamServiceId, serviceId)
                        .WithParameter(ParamSince, since.Value)
                    : new QueryDefinition($"SELECT VALUE COUNT(1) FROM c WHERE c.serviceId = {ParamServiceId}")
                        .WithParameter(ParamServiceId, serviceId);
            }
            using var iterator = container.GetItemQueryIterator<long>(queryDefinition);

            long count = 0;
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync().ConfigureAwait(false);
                count += response.FirstOrDefault();
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
            var serviceId = CurrentServiceId;
            var settings = _containerResolver.ResolveTagsContainer(serviceId);
            var tagsContainer = await _context.GetTagsContainerAsync(settings).ConfigureAwait(false);

            // Query all tags and group in memory (Cosmos DB doesn't support complex GROUP BY with aggregations)
            IQueryable<CosmosTag> query = tagsContainer.GetItemLinqQueryable<CosmosTag>();
            if (!settings.IsLegacy)
            {
                query = query.Where(t => t.ServiceId == serviceId);
            }
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

    // LoggerMessage methods for high-performance logging (CA1848)
    [LoggerMessage(Level = LogLevel.Error, Message = "WriteEventsAsync failed with CosmosException: {Message}")]
    private static partial void LogWriteEventsCosmosError(ILogger logger, Exception ex, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "WriteEventsAsync failed: {Message}")]
    private static partial void LogWriteEventsFailed(ILogger logger, Exception ex, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to rollback {FailedCount} events after tag write failure. Orphaned event IDs: {OrphanedIds}")]
    private static partial void LogRollbackFailed(ILogger logger, int failedCount, string orphanedIds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully rolled back {Count} events after tag write failure")]
    private static partial void LogRollbackSucceeded(ILogger logger, int count);
}
