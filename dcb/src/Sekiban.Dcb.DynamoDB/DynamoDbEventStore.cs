using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.DynamoDB.Models;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Tags;
using System.Security.Cryptography;
using System.Text;

namespace Sekiban.Dcb.DynamoDB;

#pragma warning disable CA1031

/// <summary>
///     DynamoDB-backed event store implementation.
/// </summary>
public partial class DynamoDbEventStore : IHotEventStore, IStorageDurabilityDescriptorProvider,
    IConditionalEventStore, IWriteConditionCapabilityProvider, IStreamingTaggedSerializableEventStore,
    ITaggedStreamCapabilityProvider
{
    private const string ConditionalProviderName = "DynamoDB";

    /// <summary>Events land in DynamoDB.</summary>
    public StorageDurabilityDescriptor DescribeStorage() =>
        new(StorageDurability.Durable, "DynamoDB");

    private readonly ConditionalAppendCoordinator _conditionalAppend;

    /// <summary>
    ///     SEK-G16 conditional (unique-key) append. The claim event is written under the deterministic id, so the existing
    ///     item primary key (<c>pk = SERVICE#{serviceId}#EVENT#{id}</c>) is the uniqueness primitive — no schema change.
    ///     The claim is a single-item transaction guarded by <c>attribute_not_exists(pk)</c>; a pre-existing claim surfaces
    ///     as <see cref="ConditionalCheckFailedException" /> (or a <see cref="TransactionCanceledException" /> whose reason
    ///     is a conditional-check failure), which is classified by fingerprint against the CONSISTENT-read winner. No
    ///     <c>ClientRequestToken</c> is used, so a same-operation retry surfaces the real conflict and is verified by
    ///     read-back rather than being silently idempotency-collapsed by DynamoDB. The unconditional write path is
    ///     untouched.
    /// </summary>
    public Task<ConditionalAppendReceipt> AppendIfUniqueAsync(
        ConditionalAppendRequest request,
        CancellationToken cancellationToken = default) =>
        _conditionalAppend.AppendIfUniqueAsync(request, cancellationToken);

    /// <inheritdoc />
    public WriteConditionCapabilityDescriptor DescribeWriteConditions() => _conditionalAppend.Descriptor;

    private async Task<ConditionalWriteOutcome> TryWriteConditionalClaimAsync(
        Guid deterministicId,
        SerializableEvent claimEvent,
        CancellationToken cancellationToken)
    {
        await _context.EnsureTablesAsync().ConfigureAwait(false);
        var serviceId = CurrentServiceId;
        var mapped = DynamoDbEventItemMapper.FromSerializableEvent(
            claimEvent,
            serviceId,
            _options.WriteShardCount,
            deterministicId);
        var dynEvent = mapped.DynamoEvent;
        var dynamoTags = mapped.DynamoTags;

        // The event Put is ALWAYS index 0 — only its attribute_not_exists(pk) condition is the claim guard, so only a
        // cancellation reason at index 0 classifies as a conflict (see the catch below).
        var transactItems = new List<TransactWriteItem>
        {
            new TransactWriteItem
            {
                Put = new Put
                {
                    TableName = _context.EventsTableName,
                    Item = dynEvent.ToAttributeValues(),
                    ConditionExpression = DynamoDbTransactionSizeAccounting.EventPutConditionExpression
                }
            }
        };
        foreach (var tag in dynamoTags)
        {
            transactItems.Add(new TransactWriteItem
            {
                Put = new Put
                {
                    TableName = _context.TagsTableName,
                    Item = tag.ToAttributeValues()
                }
            });
        }

        // Enforce DynamoDB's TransactWriteItems limits BEFORE the call, fail-closed: >100 items, or a duplicate item key
        // (a transaction may not touch the same item twice — e.g. duplicate tag strings). A violation is a permanent
        // request error, never a claim conflict or a retryable in-doubt.
        EnforceTransactWriteLimits(transactItems);

        // Pre-dispatch cancellation boundary (OUTSIDE the try below): an already-cancelled token is a KNOWN no-commit — no
        // SDK request is dispatched — so it surfaces the exact original OperationCanceledException/token, never the
        // post-commit ambiguity marker. Only a cancellation/timeout raised BY the dispatch (below) is unknown-commit.
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems },
                cancellationToken).ConfigureAwait(false);
            return ConditionalWriteOutcome.Wrote();
        }
        catch (ConditionalCheckFailedException ex)
        {
            // A bare ConditionalCheckFailed on a single-item PutItem-style rejection maps to the event claim guard.
            return ConditionalWriteOutcome.Conflict(ex);
        }
        catch (TransactionCanceledException ex) when (
            ex.CancellationReasons.Count > 0 &&
            string.Equals(ex.CancellationReasons[0].Code, "ConditionalCheckFailed", StringComparison.Ordinal))
        {
            // Only the event Put (index 0) carries the attribute_not_exists condition, so only a conditional failure at
            // index 0 is the claim collision. A cancellation for any other reason (throttle, size, a different index)
            // preserves its original failure and is never misrouted to winner classification.
            return ConditionalWriteOutcome.Conflict(ex);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            // TransactWriteItems IS the commit, so a cancellation/timeout here is an UNKNOWN commit status (the request may
            // have reached the server and committed). Signal the post-commit ambiguity marker so the shared orchestrator
            // resolves it by authoritative read-back rather than surfacing a possibly-false pre-commit failure.
            throw new PostCommitResponseLostException(ex);
        }
    }

    private static void EnforceTransactWriteLimits(List<TransactWriteItem> transactItems)
    {
        if (transactItems.Count > DynamoConditionalAppendLimitException.MaxTransactItems)
        {
            throw new DynamoConditionalAppendLimitException(
                $"Conditional append would write {transactItems.Count} transaction items (1 event + tags), exceeding "
                + $"DynamoDB's TransactWriteItems limit of {DynamoConditionalAppendLimitException.MaxTransactItems}.");
        }

        var seen = new HashSet<(string, string)>();
        foreach (var item in transactItems)
        {
            var key = (item.Put.Item["pk"].S, item.Put.Item["sk"].S);
            if (!seen.Add(key))
            {
                throw new DynamoConditionalAppendLimitException(
                    "Conditional append would write the same DynamoDB item more than once in one transaction "
                    + "(duplicate item key); DynamoDB rejects duplicate item operations. Deduplicate the event's tags.");
            }
        }
    }

    private async Task<SerializableEvent?> ReadConditionalWinnerAsync(
        Guid deterministicId,
        CancellationToken cancellationToken)
    {
        await _context.EnsureTablesAsync().ConfigureAwait(false);
        var serviceId = CurrentServiceId;
        // Consistent read: the winner may have committed a moment ago; an eventually-consistent read could miss it and
        // wrongly report the claim as in-doubt.
        var response = await _client.GetItemAsync(
            new GetItemRequest
            {
                TableName = _context.EventsTableName,
                Key = BuildEventKey(serviceId, deterministicId.ToString()),
                ConsistentRead = true
            },
            cancellationToken).ConfigureAwait(false);

        if (response.Item == null || response.Item.Count == 0)
        {
            return null;
        }

        var dynEvent = DynamoEvent.FromAttributeValues(response.Item);
        return new SerializableEvent(
            Encoding.UTF8.GetBytes(dynEvent.Payload),
            dynEvent.SortableUniqueId,
            Guid.Parse(dynEvent.EventId),
            new EventMetadata(
                dynEvent.CausationId ?? string.Empty,
                dynEvent.CorrelationId ?? string.Empty,
                dynEvent.ExecutedUser ?? string.Empty),
            dynEvent.Tags.ToList(),
            dynEvent.EventType);
    }

    private const string PartitionKeyConditionExpression = "gsi1pk = :pk";
    private const string PartitionKeySinceConditionExpression = "gsi1pk = :pk AND sortableUniqueId > :since";

    private static readonly Action<ILogger, string, Exception?> LogWriteFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, nameof(LogWriteFailed)),
            "DynamoDB WriteEventsAsync failed: {Message}");

    private static readonly Action<ILogger, string, Exception?> LogReadFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(2, nameof(LogReadFailed)),
            "DynamoDB read failed: {Message}");

    private readonly DynamoDbContext _context;
    private readonly IEventTypes _eventTypes;
    private readonly ILogger<DynamoDbEventStore>? _logger;
    private readonly DynamoDbEventStoreOptions _options;
    private readonly IAmazonDynamoDB _client;
    private readonly IServiceIdProvider _serviceIdProvider;

    /// <summary>
    ///     Initializes a new DynamoDbEventStore.
    /// </summary>
    public DynamoDbEventStore(
        DynamoDbContext context,
        IEventTypes eventTypes,
        IServiceIdProvider serviceIdProvider,
        ILogger<DynamoDbEventStore>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _eventTypes = eventTypes ?? throw new ArgumentNullException(nameof(eventTypes));
        _serviceIdProvider = serviceIdProvider ?? throw new ArgumentNullException(nameof(serviceIdProvider));
        _logger = logger;
        _options = context.Options;
        _client = context.Client;
        _conditionalAppend = new ConditionalAppendCoordinator(
            ConditionalProviderName, () => CurrentServiceId, _eventTypes,
            TryWriteConditionalClaimAsync, ReadConditionalWinnerAsync);
    }

    private string CurrentServiceId => _serviceIdProvider.GetCurrentServiceId();

    /// <summary>
    ///     Reads all events ordered by sortable unique ID.
    /// </summary>
    public async Task<IEnumerable<Event>> ReadAllEventsAsync(SortableUniqueId? since = null, int? maxCount = null)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var shardCount = Math.Max(1, _options.WriteShardCount);
            var shardKeys = shardCount == 1
                ? new[] { BuildEventsGsiPartitionKey(serviceId) }
                : Enumerable.Range(0, shardCount)
                    .Select(i => BuildEventsGsiPartitionKey(serviceId, i))
                    .ToArray();

            var allEvents = new List<Event>();
            foreach (var shardKey in shardKeys)
            {
                var events = await QueryEventsByShardAsync(shardKey, since).ConfigureAwait(false);
                allEvents.AddRange(events);
            }

            var ordered = allEvents
                .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal)
                .ToList();

            if (maxCount.HasValue)
            {
                ordered = ordered.Take(maxCount.Value).ToList();
            }

            return ordered;
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            throw;
        }
    }

    /// <summary>
    ///     Reads events for a given tag.
    /// </summary>
    public async Task<IEnumerable<Event>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        ArgumentNullException.ThrowIfNull(tag);
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var tagString = tag.GetTag();
            var tagPk = BuildTagPk(serviceId, tagString);
            var tagItems = await QueryTagsAsync(tagPk, since).ConfigureAwait(false);

            if (tagItems.Count == 0)
                return Array.Empty<Event>();

            var eventIds = tagItems.Select(t => t.EventId).Distinct().ToList();
            var eventsById = await BatchGetEventsAsync(serviceId, eventIds).ConfigureAwait(false);

            var result = new List<Event>(tagItems.Count);
            foreach (var tagItem in tagItems)
            {
                if (!eventsById.TryGetValue(tagItem.EventId, out var dynEvent))
                {
                    throw new InvalidOperationException($"Event not found for tag entry: {tagItem.EventId}");
                }

                var payload = DeserializeEventPayload(dynEvent.EventType, dynEvent.Payload);

                result.Add(dynEvent.ToEvent(payload));
            }

            return result;
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            throw;
        }
    }

    /// <summary>
    ///     Reads a single event by ID. Returns <c>null</c> when no event with <paramref name="eventId" /> exists.
    /// </summary>
    public async Task<Event?> ReadEventAsync(Guid eventId)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var key = BuildEventKey(serviceId, eventId.ToString());
            var response = await _client.GetItemAsync(new GetItemRequest
            {
                TableName = _context.EventsTableName,
                Key = key,
                ConsistentRead = _options.UseConsistentReads
            }).ConfigureAwait(false);

            if (response.Item == null || response.Item.Count == 0)
                return null;

            var dynEvent = DynamoEvent.FromAttributeValues(response.Item);
            var payload = DeserializeEventPayload(dynEvent.EventType, dynEvent.Payload);

            return dynEvent.ToEvent(payload);
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            throw;
        }
    }

    /// <summary>
    ///     Writes events and associated tag records.
    /// </summary>
    public async Task<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)> WriteEventsAsync(
        IEnumerable<Event> events)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var eventsList = events.ToList();
            if (eventsList.Count == 0)
            {
                return (Events: (IReadOnlyList<Event>)Array.Empty<Event>(),
                        TagWrites: (IReadOnlyList<TagWriteResult>)Array.Empty<TagWriteResult>());
            }

            var eventItems = eventsList
                .Select(ev => DynamoDbEventItemMapper.FromEvent(
                    ev,
                    SerializeEventPayload(ev.Payload),
                    serviceId,
                    _options.WriteShardCount,
                    DateTimeOffset.UtcNow))
                .ToList();

            var batches = BuildTransactionBatches(eventItems);
            if (batches == null)
            {
                await WriteEventsWithBatchAsync(eventItems).ConfigureAwait(false);
            }
            else
            {
                await WriteEventsWithTransactionsAsync(batches).ConfigureAwait(false);
            }

            var tagWrites = BuildTagWriteResults(eventsList);
            return (
                Events: (IReadOnlyList<Event>)eventsList,
                TagWrites: (IReadOnlyList<TagWriteResult>)tagWrites);
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogWriteFailed(_logger, ex.Message, ex);
            throw;
        }
    }

    /// <summary>
    ///     Reads tag streams for the specified tag.
    /// </summary>
    public async Task<IEnumerable<TagStream>> ReadTagsAsync(ITag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var tagString = tag.GetTag();
            var tagPk = BuildTagPk(serviceId, tagString);
            var tagItems = await QueryTagsAsync(tagPk, null).ConfigureAwait(false);

            var streams = tagItems
                .Select(t => new TagStream(t.TagString, Guid.Parse(t.EventId), t.SortableUniqueId))
                .ToList();

            return streams;
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            throw;
        }
    }

    /// <summary>
    ///     Gets the latest tag state for the specified tag.
    /// </summary>
    public async Task<TagState> GetLatestTagAsync(ITag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var tagString = tag.GetTag();
            var tagPk = BuildTagPk(serviceId, tagString);

            var request = new QueryRequest
            {
                TableName = _context.TagsTableName,
                KeyConditionExpression = "pk = :pk",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue { S = tagPk }
                },
                ScanIndexForward = false,
                Limit = 1,
                ConsistentRead = _options.UseConsistentReads
            };

            var response = await _client.QueryAsync(request).ConfigureAwait(false);
            var latest = response.Items.FirstOrDefault();

            if (latest == null)
            {
                return new TagState(
                        new EmptyTagStatePayload(),
                        0,
                        string.Empty,
                        tag.GetTagGroup(),
                        tag.GetTagContent(),
                        string.Empty,
                        string.Empty);
            }

            var dynTag = DynamoTag.FromAttributeValues(latest);
            return new TagState(
                    new EmptyTagStatePayload(),
                    0,
                    dynTag.SortableUniqueId,
                    tag.GetTagGroup(),
                    tag.GetTagContent(),
                    string.Empty,
                    string.Empty);
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            throw;
        }
    }

    /// <summary>
    ///     Checks whether a tag exists.
    /// </summary>
    public async Task<bool> TagExistsAsync(ITag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var tagString = tag.GetTag();
            var tagPk = BuildTagPk(serviceId, tagString);

            var response = await _client.QueryAsync(new QueryRequest
            {
                TableName = _context.TagsTableName,
                KeyConditionExpression = "pk = :pk",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue { S = tagPk }
                },
                Limit = 1,
                ConsistentRead = _options.UseConsistentReads
            }).ConfigureAwait(false);

            return response.Count > 0;
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            throw;
        }
    }

    /// <summary>
    ///     Returns the total event count optionally after a specific sortable ID.
    /// </summary>
    public async Task<long> GetEventCountAsync(SortableUniqueId? since = null)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var shardCount = Math.Max(1, _options.WriteShardCount);
            var shardKeys = shardCount == 1
                ? new[] { BuildEventsGsiPartitionKey(serviceId) }
                : Enumerable.Range(0, shardCount)
                    .Select(i => BuildEventsGsiPartitionKey(serviceId, i))
                    .ToArray();

            long total = 0;
            foreach (var shardKey in shardKeys)
            {
                var count = await QueryEventCountAsync(shardKey, since).ConfigureAwait(false);
                total += count;
            }

            return total;
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            throw;
        }
    }

    public async Task<string> GetLatestSortableUniqueIdAsync()
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var shardCount = Math.Max(1, _options.WriteShardCount);
            var shardKeys = shardCount == 1
                ? new[] { BuildEventsGsiPartitionKey(serviceId) }
                : Enumerable.Range(0, shardCount)
                    .Select(i => BuildEventsGsiPartitionKey(serviceId, i))
                    .ToArray();

            var tasks = shardKeys.Select(async shardKey =>
            {
                var request = new QueryRequest
                {
                    TableName = _context.EventsTableName,
                    IndexName = DynamoDbContext.EventsGsiName,
                    KeyConditionExpression = PartitionKeyConditionExpression,
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":pk"] = new AttributeValue { S = shardKey }
                    },
                    ScanIndexForward = false,
                    Limit = 1,
                    ProjectionExpression = "sortableUniqueId"
                };

                var response = await _client.QueryAsync(request).ConfigureAwait(false);
                if (response.Items.Count > 0 &&
                    response.Items[0].TryGetValue("sortableUniqueId", out var attr))
                {
                    return attr.S;
                }
                return string.Empty;
            });

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            var latest = results
                .Where(id => !string.IsNullOrEmpty(id))
                .DefaultIfEmpty(string.Empty)
                .Max(StringComparer.Ordinal) ?? string.Empty;

            return latest;
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            throw;
        }
    }

    /// <summary>
    ///     Returns all tag infos, optionally filtered by tag group.
    /// </summary>
    public async Task<IEnumerable<TagInfo>> GetAllTagsAsync(string? tagGroup = null)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var tagItems = await ReadAllTagItemsAsync(serviceId, tagGroup).ConfigureAwait(false);

            var grouped = tagItems
                .GroupBy(t => t.TagString)
                .Select(g =>
                {
                    var first = g.OrderBy(x => x.SortableUniqueId, StringComparer.Ordinal).First();
                    var last = g.OrderByDescending(x => x.SortableUniqueId, StringComparer.Ordinal).First();

                    DateTime? firstAt = DateTime.TryParse(first.CreatedAt, out var fa) ? fa : null;
                    DateTime? lastAt = DateTime.TryParse(last.CreatedAt, out var la) ? la : null;

                    return new TagInfo(
                        g.Key,
                        ToDisplayTagGroup(serviceId, g.First().TagGroup),
                        g.Count(),
                        first.SortableUniqueId,
                        last.SortableUniqueId,
                        firstAt,
                        lastAt);
                })
                .OrderBy(t => t.TagGroup)
                .ThenBy(t => t.Tag)
                .ToList();

            return grouped;
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            throw;
        }
    }

    private async Task<List<Event>> QueryEventsByShardAsync(string shardKey, SortableUniqueId? since)
    {
        var events = new List<Event>();
        Dictionary<string, AttributeValue>? lastKey = null;
        var totalRead = 0L;
        var totalConsumed = 0d;

        do
        {
            var request = new QueryRequest
            {
                TableName = _context.EventsTableName,
                IndexName = DynamoDbContext.EventsGsiName,
                KeyConditionExpression = since == null
                    ? PartitionKeyConditionExpression
                    : PartitionKeySinceConditionExpression,
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue { S = shardKey }
                },
                ScanIndexForward = true,
                Limit = _options.QueryPageSize,
                ExclusiveStartKey = lastKey,
                ReturnConsumedCapacity = _options.ReadProgressCallback != null
                    ? ReturnConsumedCapacity.TOTAL
                    : ReturnConsumedCapacity.NONE
            };

            if (since != null)
            {
                request.ExpressionAttributeValues[":since"] = new AttributeValue { S = since.Value };
            }

            var response = await _client.QueryAsync(request).ConfigureAwait(false);
            foreach (var item in response.Items)
            {
                var dynEvent = DynamoEvent.FromAttributeValues(item);
                var payload = DeserializeEventPayload(dynEvent.EventType, dynEvent.Payload);
                events.Add(dynEvent.ToEvent(payload));
            }

            totalRead += response.Count ?? 0;
            if (response.ConsumedCapacity != null)
                totalConsumed += response.ConsumedCapacity.CapacityUnits ?? 0d;

            _options.ReadProgressCallback?.Invoke(totalRead, totalConsumed);
            lastKey = response.LastEvaluatedKey;
        } while (lastKey != null && lastKey.Count > 0);

        return events;
    }

    private async Task<long> QueryEventCountAsync(string shardKey, SortableUniqueId? since)
    {
        Dictionary<string, AttributeValue>? lastKey = null;
        long total = 0;

        do
        {
            var request = new QueryRequest
            {
                TableName = _context.EventsTableName,
                IndexName = DynamoDbContext.EventsGsiName,
                KeyConditionExpression = since == null
                    ? PartitionKeyConditionExpression
                    : PartitionKeySinceConditionExpression,
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue { S = shardKey }
                },
                Select = Select.COUNT,
                ExclusiveStartKey = lastKey
            };

            if (since != null)
            {
                request.ExpressionAttributeValues[":since"] = new AttributeValue { S = since.Value };
            }

            var response = await _client.QueryAsync(request).ConfigureAwait(false);
            total += response.Count ?? 0;
            lastKey = response.LastEvaluatedKey;
        } while (lastKey != null && lastKey.Count > 0);

        return total;
    }

    private async Task<List<DynamoTag>> QueryTagsAsync(string tagPk, SortableUniqueId? since)
    {
        var tags = new List<DynamoTag>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var request = new QueryRequest
            {
                TableName = _context.TagsTableName,
                KeyConditionExpression = since == null
                    ? "pk = :pk"
                    : "pk = :pk AND sk > :since",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue { S = tagPk }
                },
                ScanIndexForward = true,
                Limit = _options.QueryPageSize,
                ExclusiveStartKey = lastKey,
                ConsistentRead = _options.UseConsistentReads
            };

            if (since != null)
            {
                request.ExpressionAttributeValues[":since"] = new AttributeValue { S = $"{since.Value}#" };
            }

            var response = await _client.QueryAsync(request).ConfigureAwait(false);
            foreach (var item in response.Items)
            {
                if (!item.ContainsKey("eventId"))
                    continue;
                tags.Add(DynamoTag.FromAttributeValues(item));
            }

            lastKey = response.LastEvaluatedKey;
        } while (lastKey != null && lastKey.Count > 0);

        return tags;
    }

    private async Task<Dictionary<string, DynamoEvent>> BatchGetEventsAsync(string serviceId, List<string> eventIds)
    {
        var results = new Dictionary<string, DynamoEvent>(StringComparer.Ordinal);
        if (eventIds.Count == 0)
            return results;

        var batchSize = ResolveBatchGetChunkSize();
        for (var i = 0; i < eventIds.Count; i += batchSize)
        {
            var chunk = eventIds.Skip(i).Take(batchSize).ToList();
            var keys = chunk
                .Select(id => BuildEventKey(serviceId, id))
                .ToList();

            var requestItems = new Dictionary<string, KeysAndAttributes>
            {
                [_context.EventsTableName] = new KeysAndAttributes
                {
                    Keys = keys,
                    ConsistentRead = _options.UseConsistentReads
                }
            };

            var items = await ExecuteBatchGetWithRetryAsync(requestItems).ConfigureAwait(false);
            foreach (var item in items)
            {
                var dynEvent = DynamoEvent.FromAttributeValues(item);
                results[dynEvent.EventId] = dynEvent;
            }
        }

        return results;
    }

    private async Task<List<Dictionary<string, AttributeValue>>> ExecuteBatchGetWithRetryAsync(
        Dictionary<string, KeysAndAttributes> requestItems)
    {
        var collected = new List<Dictionary<string, AttributeValue>>();
        var pending = requestItems;

        for (var attempt = 0; attempt <= _options.MaxRetryAttempts; attempt++)
        {
            var response = await _client.BatchGetItemAsync(new BatchGetItemRequest
            {
                RequestItems = pending
            }).ConfigureAwait(false);

            if (response.Responses.TryGetValue(_context.EventsTableName, out var items))
            {
                collected.AddRange(items);
            }

            if (response.UnprocessedKeys == null || response.UnprocessedKeys.Count == 0)
                return collected;

            pending = response.UnprocessedKeys;
            await Task.Delay(ComputeBackoff(attempt)).ConfigureAwait(false);
        }

        throw new InvalidOperationException("BatchGetItem exceeded retry limit.");
    }

    private List<List<DynamoDbEventWriteItem>>? BuildTransactionBatches(List<DynamoDbEventWriteItem> eventItems)
    {
        var batches = new List<List<DynamoDbEventWriteItem>>();
        var current = new List<DynamoDbEventWriteItem>();
        var currentCount = 0;

        foreach (var item in eventItems)
        {
            var itemCount = 1 + item.DynamoTags.Count;
            if (itemCount > _options.MaxTransactionItems)
                return null;

            if (currentCount + itemCount > _options.MaxTransactionItems)
            {
                batches.Add(current);
                current = new List<DynamoDbEventWriteItem>();
                currentCount = 0;
            }

            current.Add(item);
            currentCount += itemCount;
        }

        if (current.Count > 0)
            batches.Add(current);

        return batches;
    }

    private async Task WriteEventsWithTransactionsAsync(List<List<DynamoDbEventWriteItem>> batches)
    {
        foreach (var batch in batches)
        {
            var transactItems = new List<TransactWriteItem>();
            var eventIdsForToken = new List<string>();

            foreach (var item in batch)
            {
                transactItems.Add(new TransactWriteItem
                {
                    Put = new Put
                    {
                        TableName = _context.EventsTableName,
                        Item = item.DynamoEvent.ToAttributeValues(),
                        ConditionExpression = DynamoDbTransactionSizeAccounting.EventPutConditionExpression
                    }
                });
                eventIdsForToken.Add(item.DynamoEvent.EventId);

                foreach (var tag in item.DynamoTags)
                {
                    transactItems.Add(new TransactWriteItem
                    {
                        Put = new Put
                        {
                            TableName = _context.TagsTableName,
                            Item = tag.ToAttributeValues()
                        }
                    });
                }
            }

            var token = ComputeIdempotencyToken(eventIdsForToken);
            await _client.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems = transactItems,
                ClientRequestToken = token
            }).ConfigureAwait(false);
        }
    }

    private async Task WriteEventsWithBatchAsync(List<DynamoDbEventWriteItem> eventItems)
    {
        var chunkSize = ResolveBatchWriteChunkSize();
        var eventWrites = eventItems
            .Select(item => new WriteRequest
            {
                PutRequest = new PutRequest { Item = item.DynamoEvent.ToAttributeValues() }
            })
            .ToList();

        var tagWrites = eventItems
            .SelectMany(item => item.DynamoTags)
            .Select(tag => new WriteRequest
            {
                PutRequest = new PutRequest { Item = tag.ToAttributeValues() }
            })
            .ToList();

        await ExecuteBatchWriteWithRetryAsync(_context.EventsTableName, eventWrites, chunkSize).ConfigureAwait(false);

        try
        {
            await ExecuteBatchWriteWithRetryAsync(_context.TagsTableName, tagWrites, chunkSize).ConfigureAwait(false);
        }
        catch
        {
            if (_options.TryRollbackOnFailure)
            {
                var deleteRequests = eventItems
                    .Select(item => new WriteRequest
                    {
                        DeleteRequest = new DeleteRequest { Key = BuildEventKey(item.DynamoEvent.ServiceId, item.DynamoEvent.EventId) }
                    })
                    .ToList();

                await ExecuteBatchWriteWithRetryAsync(_context.EventsTableName, deleteRequests, chunkSize).ConfigureAwait(false);
            }

            throw;
        }
    }

    private int ResolveBatchWriteChunkSize()
    {
        var chunkSize = _options.MaxBatchWriteItems;
        if (chunkSize is < 1 or > 25)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DynamoDbEventStoreOptions.MaxBatchWriteItems),
                chunkSize,
                "DynamoDB batch writes require between 1 and 25 items per request.");
        }

        return chunkSize;
    }

    private async Task ExecuteBatchWriteWithRetryAsync(
        string tableName,
        List<WriteRequest> requests,
        int chunkSize)
    {
        for (var i = 0; i < requests.Count; i += chunkSize)
        {
            var batch = requests.Skip(i).Take(chunkSize).ToList();
            var pending = new Dictionary<string, List<WriteRequest>>
            {
                [tableName] = batch
            };

            for (var attempt = 0; attempt <= _options.MaxRetryAttempts; attempt++)
            {
                var response = await _client.BatchWriteItemAsync(new BatchWriteItemRequest
                {
                    RequestItems = pending
                }).ConfigureAwait(false);

                if (response.UnprocessedItems == null || response.UnprocessedItems.Count == 0)
                {
                    pending = new Dictionary<string, List<WriteRequest>>();
                    break;
                }

                pending = response.UnprocessedItems;
                await Task.Delay(ComputeBackoff(attempt)).ConfigureAwait(false);
            }

            if (pending.Count > 0)
            {
                throw new InvalidOperationException("BatchWriteItem exceeded retry limit.");
            }
        }
    }

    private async Task<List<DynamoTag>> ReadAllTagItemsAsync(string serviceId, string? tagGroup)
    {
        return tagGroup != null
            ? await ReadTagItemsByGroupAsync(serviceId, tagGroup).ConfigureAwait(false)
            : await ReadTagItemsByScanAsync(serviceId).ConfigureAwait(false);
    }

    private async Task<List<DynamoTag>> ReadTagItemsByGroupAsync(string serviceId, string tagGroup)
    {
        var tags = new List<DynamoTag>();
        Dictionary<string, AttributeValue>? lastKey = null;
        var storedTagGroup = BuildStoredTagGroup(serviceId, tagGroup);

        do
        {
            var response = await _client.QueryAsync(new QueryRequest
            {
                TableName = _context.TagsTableName,
                IndexName = DynamoDbContext.TagsGsiName,
                KeyConditionExpression = "tagGroup = :group",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":group"] = new AttributeValue { S = storedTagGroup },
                    [":serviceId"] = new AttributeValue { S = serviceId }
                },
                FilterExpression = "serviceId = :serviceId",
                ExclusiveStartKey = lastKey,
                Limit = _options.QueryPageSize
            }).ConfigureAwait(false);

            AppendTags(response.Items, tags);
            lastKey = response.LastEvaluatedKey;
        } while (lastKey != null && lastKey.Count > 0);

        return tags;
    }

    private async Task<List<DynamoTag>> ReadTagItemsByScanAsync(string serviceId)
    {
        var tags = new List<DynamoTag>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = await _client.ScanAsync(new ScanRequest
            {
                TableName = _context.TagsTableName,
                FilterExpression = "serviceId = :serviceId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":serviceId"] = new AttributeValue { S = serviceId }
                },
                ExclusiveStartKey = lastKey,
                Limit = _options.QueryPageSize
            }).ConfigureAwait(false);

            AppendTags(response.Items, tags);
            lastKey = response.LastEvaluatedKey;
        } while (lastKey != null && lastKey.Count > 0);

        return tags;
    }

    private static void AppendTags(List<Dictionary<string, AttributeValue>> items, List<DynamoTag> tags)
    {
        foreach (var item in items)
        {
            if (!item.ContainsKey("eventId"))
                continue;
            tags.Add(DynamoTag.FromAttributeValues(item));
        }
    }

    private static Dictionary<string, AttributeValue> BuildEventKey(string serviceId, string eventId)
    {
        return new Dictionary<string, AttributeValue>
        {
            ["pk"] = new AttributeValue { S = $"SERVICE#{serviceId}#EVENT#{eventId}" },
            ["sk"] = new AttributeValue { S = $"EVENT#{eventId}" }
        };
    }

    private static List<TagWriteResult> BuildTagWriteResults(IEnumerable<Event> events)
    {
        var now = DateTimeOffset.UtcNow;
        return events
            .SelectMany(e => e.Tags)
            .Distinct(StringComparer.Ordinal)
            .Select(tag => new TagWriteResult(tag, 1, now))
            .ToList();
    }

    private string SerializeEventPayload(IEventPayload payload) =>
        _eventTypes.SerializeEventPayload(payload);

    private IEventPayload DeserializeEventPayload(string eventType, string json) =>
        _eventTypes.DeserializeEventPayload(eventType, json)
        ?? throw new InvalidOperationException(
            $"Failed to deserialize event payload of type {eventType}. " +
            "Make sure the event type is registered in DcbDomainTypes.");

    private string GetGsiPartitionKey(string sortableUniqueId, string serviceId)
    {
        if (_options.WriteShardCount <= 1)
            return BuildEventsGsiPartitionKey(serviceId);

        var shard = ComputeShardIndex(sortableUniqueId, _options.WriteShardCount);
        return BuildEventsGsiPartitionKey(serviceId, shard);
    }

    private static string BuildEventsGsiPartitionKey(string serviceId, int? shard = null)
    {
        var baseKey = $"SERVICE#{serviceId}#{DynamoDbContext.EventsGsiPartitionKey}";
        return shard.HasValue ? $"{baseKey}#{shard.Value}" : baseKey;
    }

    private static string BuildTagPk(string serviceId, string tagString) =>
        $"SERVICE#{serviceId}#TAG#{tagString}";

    private static string BuildStoredTagGroup(string serviceId, string tagGroup) =>
        $"{serviceId}|{tagGroup}";

    private static string ToDisplayTagGroup(string serviceId, string storedTagGroup)
    {
        var prefix = $"{serviceId}|";
        return storedTagGroup.StartsWith(prefix, StringComparison.Ordinal)
            ? storedTagGroup[prefix.Length..]
            : storedTagGroup;
    }

    private static int ComputeShardIndex(string value, int shardCount)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var hashInt = BitConverter.ToInt32(hash, 0) & int.MaxValue;
        return hashInt % shardCount;
    }

    private static string ComputeIdempotencyToken(IEnumerable<string> values)
    {
        var joined = string.Join("|", values);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        var hex = Convert.ToHexString(hash);
        return hex.Length > 36 ? hex[..36] : hex;
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        var delay = TimeSpan.FromMilliseconds(Math.Min(200 * Math.Pow(2, attempt), _options.MaxRetryDelay.TotalMilliseconds));
        return delay;
    }

    /// <summary>
    ///     Reads all events as SerializableEvent (no payload deserialization).
    /// </summary>
    public async Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null)
        => await ReadAllSerializableEventsAsync(since, maxCount: null).ConfigureAwait(false);

    /// <summary>
    ///     Reads all events as SerializableEvent (no payload deserialization), with optional upper limit.
    /// </summary>
    /// <param name="since">Optional: only return events after this ID.</param>
    /// <param name="maxCount">Optional: maximum number of events to return.</param>
    public async Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var shardCount = Math.Max(1, _options.WriteShardCount);
            var shardKeys = shardCount == 1
                ? new[] { BuildEventsGsiPartitionKey(serviceId) }
                : Enumerable.Range(0, shardCount)
                    .Select(i => BuildEventsGsiPartitionKey(serviceId, i))
                    .ToArray();

            var allEvents = new List<SerializableEvent>();
            foreach (var shardKey in shardKeys)
            {
                var events = await QuerySerializableEventsByShardAsync(shardKey, since).ConfigureAwait(false);
                allEvents.AddRange(events);
            }

            var ordered = allEvents
                .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal)
                .ToList();

            if (maxCount.HasValue)
            {
                ordered = ordered.Take(maxCount.Value).ToList();
            }

            return ordered;
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            throw;
        }
    }

    /// <summary>
    ///     Reads events for a given tag as SerializableEvent (no payload deserialization).
    /// </summary>
    public async Task<IEnumerable<SerializableEvent>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        ArgumentNullException.ThrowIfNull(tag);
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var tagString = tag.GetTag();
            var tagPk = BuildTagPk(serviceId, tagString);
            var tagItems = await QueryTagsAsync(tagPk, since).ConfigureAwait(false);

            if (tagItems.Count == 0)
                return Array.Empty<SerializableEvent>();

            var eventIds = tagItems.Select(t => t.EventId).Distinct().ToList();
            var eventsById = await BatchGetEventsAsync(serviceId, eventIds).ConfigureAwait(false);

            var result = new List<SerializableEvent>(tagItems.Count);
            foreach (var tagItem in tagItems)
            {
                if (!eventsById.TryGetValue(tagItem.EventId, out var dynEvent))
                {
                    throw new InvalidOperationException($"Event not found for tag entry: {tagItem.EventId}");
                }

                result.Add(new SerializableEvent(
                    Encoding.UTF8.GetBytes(dynEvent.Payload),
                    dynEvent.SortableUniqueId,
                    Guid.Parse(dynEvent.EventId),
                    new EventMetadata(
                        dynEvent.CausationId ?? string.Empty,
                        dynEvent.CorrelationId ?? string.Empty,
                        dynEvent.ExecutedUser ?? string.Empty),
                    dynEvent.Tags.ToList(),
                    dynEvent.EventType));
            }

            return result;
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            throw;
        }
    }

    /// <summary>
    ///     Reads a single event as <see cref="SerializableEvent" /> without exposing typed payload conversion to callers.
    ///     Returns <c>null</c> when no event with <paramref name="eventId" /> exists.
    /// </summary>
    public async Task<SerializableEvent?> ReadSerializableEventAsync(Guid eventId) =>
        (await ReadEventAsync(eventId).ConfigureAwait(false))?.ToSerializableEvent(_eventTypes);

    /// <summary>
    ///     Writes pre-serialized events and associated tag records.
    /// </summary>
    public async Task<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)> WriteSerializableEventsAsync(
        IEnumerable<SerializableEvent> events)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var eventsList = events.ToList();
            if (eventsList.Count == 0)
            {
                return (Events: (IReadOnlyList<SerializableEvent>)Array.Empty<SerializableEvent>(),
                        TagWrites: (IReadOnlyList<TagWriteResult>)Array.Empty<TagWriteResult>());
            }

            var eventItems = eventsList
                .Select(se => DynamoDbEventItemMapper.FromSerializableEvent(
                    se,
                    serviceId,
                    _options.WriteShardCount,
                    timestamp: DateTimeOffset.UtcNow))
                .ToList();

            var batches = BuildTransactionBatches(eventItems);
            if (batches == null)
            {
                await WriteEventsWithBatchAsync(eventItems).ConfigureAwait(false);
            }
            else
            {
                await WriteEventsWithTransactionsAsync(batches).ConfigureAwait(false);
            }

            var tagWrites = eventsList
                .SelectMany(e => e.Tags)
                .Distinct(StringComparer.Ordinal)
                .Select(tag => new TagWriteResult(tag, 1, DateTimeOffset.UtcNow))
                .ToList();

            return (
                Events: (IReadOnlyList<SerializableEvent>)eventsList,
                TagWrites: (IReadOnlyList<TagWriteResult>)tagWrites);
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogWriteFailed(_logger, ex.Message, ex);
            throw;
        }
    }

    private async Task<List<SerializableEvent>> QuerySerializableEventsByShardAsync(string shardKey, SortableUniqueId? since)
    {
        var events = new List<SerializableEvent>();
        Dictionary<string, AttributeValue>? lastKey = null;
        var totalRead = 0L;
        var totalConsumed = 0d;

        do
        {
            var request = new QueryRequest
            {
                TableName = _context.EventsTableName,
                IndexName = DynamoDbContext.EventsGsiName,
                KeyConditionExpression = since == null
                    ? PartitionKeyConditionExpression
                    : PartitionKeySinceConditionExpression,
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue { S = shardKey }
                },
                ScanIndexForward = true,
                Limit = _options.QueryPageSize,
                ExclusiveStartKey = lastKey,
                ReturnConsumedCapacity = _options.ReadProgressCallback != null
                    ? ReturnConsumedCapacity.TOTAL
                    : ReturnConsumedCapacity.NONE
            };

            if (since != null)
            {
                request.ExpressionAttributeValues[":since"] = new AttributeValue { S = since.Value };
            }

            var response = await _client.QueryAsync(request).ConfigureAwait(false);
            foreach (var item in response.Items)
            {
                var dynEvent = DynamoEvent.FromAttributeValues(item);
                events.Add(new SerializableEvent(
                    Encoding.UTF8.GetBytes(dynEvent.Payload),
                    dynEvent.SortableUniqueId,
                    Guid.Parse(dynEvent.EventId),
                    new EventMetadata(
                        dynEvent.CausationId ?? string.Empty,
                        dynEvent.CorrelationId ?? string.Empty,
                        dynEvent.ExecutedUser ?? string.Empty),
                    dynEvent.Tags.ToList(),
                    dynEvent.EventType));
            }

            totalRead += response.Count ?? 0;
            if (response.ConsumedCapacity != null)
                totalConsumed += response.ConsumedCapacity.CapacityUnits ?? 0d;

            _options.ReadProgressCallback?.Invoke(totalRead, totalConsumed);
            lastKey = response.LastEvaluatedKey;
        } while (lastKey != null && lastKey.Count > 0);

        return events;
    }

}
