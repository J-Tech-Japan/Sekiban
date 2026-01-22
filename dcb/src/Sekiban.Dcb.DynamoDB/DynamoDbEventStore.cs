using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.DynamoDB.Models;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Security.Cryptography;
using System.Text;

namespace Sekiban.Dcb.DynamoDB;

/// <summary>
///     DynamoDB-backed event store implementation.
/// </summary>
public class DynamoDbEventStore : IEventStore
{
    private static readonly Action<ILogger, string, Exception?> LogWriteFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, nameof(LogWriteFailed)),
            "DynamoDB WriteEventsAsync failed: {Message}");

    private static readonly Action<ILogger, string, Exception?> LogReadFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(2, nameof(LogReadFailed)),
            "DynamoDB read failed: {Message}");

    private readonly DynamoDbContext _context;
    private readonly DcbDomainTypes _domainTypes;
    private readonly ILogger<DynamoDbEventStore>? _logger;
    private readonly DynamoDbEventStoreOptions _options;
    private readonly IAmazonDynamoDB _client;

    public DynamoDbEventStore(
        DynamoDbContext context,
        DcbDomainTypes domainTypes,
        ILogger<DynamoDbEventStore>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _logger = logger;
        _options = context.Options;
        _client = context.Client;
    }

    public async Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var shardCount = Math.Max(1, _options.WriteShardCount);
            var shardKeys = shardCount == 1
                ? new[] { DynamoDbContext.EventsGsiPartitionKey }
                : Enumerable.Range(0, shardCount)
                    .Select(i => $"{DynamoDbContext.EventsGsiPartitionKey}#{i}")
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

            return ResultBox.FromValue<IEnumerable<Event>>(ordered);
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
    }

    public async Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var tagString = tag.GetTag();
            var tagPk = $"TAG#{tagString}";
            var tagItems = await QueryTagsAsync(tagPk, since).ConfigureAwait(false);

            if (tagItems.Count == 0)
                return ResultBox.FromValue<IEnumerable<Event>>(Array.Empty<Event>());

            var eventIds = tagItems.Select(t => t.EventId).Distinct().ToList();
            var eventsById = await BatchGetEventsAsync(eventIds).ConfigureAwait(false);

            var result = new List<Event>(tagItems.Count);
            foreach (var tagItem in tagItems)
            {
                if (!eventsById.TryGetValue(tagItem.EventId, out var dynEvent))
                {
                    return ResultBox.Error<IEnumerable<Event>>(
                        new InvalidOperationException($"Event not found for tag entry: {tagItem.EventId}"));
                }

                var payloadResult = DeserializeEventPayload(dynEvent.EventType, dynEvent.Payload);
                if (!payloadResult.IsSuccess)
                    return ResultBox.Error<IEnumerable<Event>>(payloadResult.GetException());

                result.Add(dynEvent.ToEvent(payloadResult.GetValue()));
            }

            return ResultBox.FromValue<IEnumerable<Event>>(result);
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
    }

    public async Task<ResultBox<Event>> ReadEventAsync(Guid eventId)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var key = BuildEventKey(eventId.ToString());
            var response = await _client.GetItemAsync(new GetItemRequest
            {
                TableName = _context.EventsTableName,
                Key = key,
                ConsistentRead = _options.UseConsistentReads
            }).ConfigureAwait(false);

            if (response.Item == null || response.Item.Count == 0)
                return ResultBox.Error<Event>(new Exception($"Event with ID {eventId} not found"));

            var dynEvent = DynamoEvent.FromAttributeValues(response.Item);
            var payloadResult = DeserializeEventPayload(dynEvent.EventType, dynEvent.Payload);
            if (!payloadResult.IsSuccess)
                return ResultBox.Error<Event>(payloadResult.GetException());

            return ResultBox.FromValue(dynEvent.ToEvent(payloadResult.GetValue()));
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            return ResultBox.Error<Event>(ex);
        }
    }

    public async Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(
        IEnumerable<Event> events)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var eventsList = events.ToList();
            if (eventsList.Count == 0)
            {
                return ResultBox.FromValue(
                    (Events: (IReadOnlyList<Event>)Array.Empty<Event>(),
                        TagWrites: (IReadOnlyList<TagWriteResult>)Array.Empty<TagWriteResult>()));
            }

            var eventItems = eventsList.Select(ev => new EventWriteItem(
                ev,
                DynamoEvent.FromEvent(
                    ev,
                    SerializeEventPayload(ev.Payload),
                    GetGsiPartitionKey(ev.SortableUniqueIdValue)),
                ev.Tags.Select(tagString =>
                {
                    var tagGroup = tagString.Contains(':', StringComparison.Ordinal)
                        ? tagString.Split(':')[0]
                        : tagString;
                    return DynamoTag.FromEventTag(tagString, tagGroup, ev.SortableUniqueIdValue, ev.Id, ev.EventType);
                }).ToList()
            )).ToList();

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
            return ResultBox.FromValue((Events: (IReadOnlyList<Event>)eventsList, TagWrites: tagWrites));
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogWriteFailed(_logger, ex.Message, ex);
            return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(ex);
        }
    }

    public async Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var tagString = tag.GetTag();
            var tagPk = $"TAG#{tagString}";
            var tagItems = await QueryTagsAsync(tagPk, null).ConfigureAwait(false);

            var streams = tagItems
                .Select(t => new TagStream(t.TagString, Guid.Parse(t.EventId), t.SortableUniqueId))
                .ToList();

            return ResultBox.FromValue<IEnumerable<TagStream>>(streams);
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            return ResultBox.Error<IEnumerable<TagStream>>(ex);
        }
    }

    public async Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var tagString = tag.GetTag();
            var tagPk = $"TAG#{tagString}";

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
                return ResultBox.FromValue(
                    new TagState(
                        new EmptyTagStatePayload(),
                        0,
                        string.Empty,
                        tag.GetTagGroup(),
                        tag.GetTagContent(),
                        string.Empty,
                        string.Empty));
            }

            var dynTag = DynamoTag.FromAttributeValues(latest);
            return ResultBox.FromValue(
                new TagState(
                    new EmptyTagStatePayload(),
                    0,
                    dynTag.SortableUniqueId,
                    tag.GetTagGroup(),
                    tag.GetTagContent(),
                    string.Empty,
                    string.Empty));
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            return ResultBox.Error<TagState>(ex);
        }
    }

    public async Task<ResultBox<bool>> TagExistsAsync(ITag tag)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var tagString = tag.GetTag();
            var tagPk = $"TAG#{tagString}";

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

            return ResultBox.FromValue(response.Count > 0);
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var shardCount = Math.Max(1, _options.WriteShardCount);
            var shardKeys = shardCount == 1
                ? new[] { DynamoDbContext.EventsGsiPartitionKey }
                : Enumerable.Range(0, shardCount)
                    .Select(i => $"{DynamoDbContext.EventsGsiPartitionKey}#{i}")
                    .ToArray();

            long total = 0;
            foreach (var shardKey in shardKeys)
            {
                var count = await QueryEventCountAsync(shardKey, since).ConfigureAwait(false);
                total += count;
            }

            return ResultBox.FromValue(total);
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            return ResultBox.Error<long>(ex);
        }
    }

    public async Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null)
    {
        try
        {
            await _context.EnsureTablesAsync().ConfigureAwait(false);

            var tagItems = await ReadAllTagItemsAsync(tagGroup).ConfigureAwait(false);

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
                        g.First().TagGroup,
                        g.Count(),
                        first.SortableUniqueId,
                        last.SortableUniqueId,
                        firstAt,
                        lastAt);
                })
                .OrderBy(t => t.TagGroup)
                .ThenBy(t => t.Tag)
                .ToList();

            return ResultBox.FromValue<IEnumerable<TagInfo>>(grouped);
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogReadFailed(_logger, ex.Message, ex);
            return ResultBox.Error<IEnumerable<TagInfo>>(ex);
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
                    ? "gsi1pk = :pk"
                    : "gsi1pk = :pk AND sortableUniqueId > :since",
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
                var payloadResult = DeserializeEventPayload(dynEvent.EventType, dynEvent.Payload);
                if (!payloadResult.IsSuccess)
                    throw payloadResult.GetException();
                events.Add(dynEvent.ToEvent(payloadResult.GetValue()));
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
                    ? "gsi1pk = :pk"
                    : "gsi1pk = :pk AND sortableUniqueId > :since",
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

    private async Task<Dictionary<string, DynamoEvent>> BatchGetEventsAsync(IReadOnlyList<string> eventIds)
    {
        var results = new Dictionary<string, DynamoEvent>(StringComparer.Ordinal);
        if (eventIds.Count == 0)
            return results;

        for (var i = 0; i < eventIds.Count; i += _options.MaxBatchGetItems)
        {
            var chunk = eventIds.Skip(i).Take(_options.MaxBatchGetItems).ToList();
            var keys = chunk
                .Select(id => BuildEventKey(id))
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

    private List<List<EventWriteItem>>? BuildTransactionBatches(List<EventWriteItem> eventItems)
    {
        var batches = new List<List<EventWriteItem>>();
        var current = new List<EventWriteItem>();
        var currentCount = 0;

        foreach (var item in eventItems)
        {
            var itemCount = 1 + item.DynamoTags.Count;
            if (itemCount > _options.MaxTransactionItems)
                return null;

            if (currentCount + itemCount > _options.MaxTransactionItems)
            {
                batches.Add(current);
                current = new List<EventWriteItem>();
                currentCount = 0;
            }

            current.Add(item);
            currentCount += itemCount;
        }

        if (current.Count > 0)
            batches.Add(current);

        return batches;
    }

    private async Task WriteEventsWithTransactionsAsync(List<List<EventWriteItem>> batches)
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
                        ConditionExpression = "attribute_not_exists(pk)"
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

    private async Task WriteEventsWithBatchAsync(List<EventWriteItem> eventItems)
    {
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

        await ExecuteBatchWriteWithRetryAsync(_context.EventsTableName, eventWrites).ConfigureAwait(false);

        try
        {
            await ExecuteBatchWriteWithRetryAsync(_context.TagsTableName, tagWrites).ConfigureAwait(false);
        }
        catch
        {
            if (_options.TryRollbackOnFailure)
            {
                var deleteRequests = eventItems
                    .Select(item => new WriteRequest
                    {
                        DeleteRequest = new DeleteRequest { Key = BuildEventKey(item.DynamoEvent.EventId) }
                    })
                    .ToList();

                await ExecuteBatchWriteWithRetryAsync(_context.EventsTableName, deleteRequests).ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task ExecuteBatchWriteWithRetryAsync(string tableName, List<WriteRequest> requests)
    {
        for (var i = 0; i < requests.Count; i += _options.MaxBatchWriteItems)
        {
            var batch = requests.Skip(i).Take(_options.MaxBatchWriteItems).ToList();
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

    private async Task<List<DynamoTag>> ReadAllTagItemsAsync(string? tagGroup)
    {
        var tags = new List<DynamoTag>();
        Dictionary<string, AttributeValue>? lastKey = null;

        if (tagGroup != null)
        {
            do
            {
                var response = await _client.QueryAsync(new QueryRequest
                {
                    TableName = _context.TagsTableName,
                    IndexName = DynamoDbContext.TagsGsiName,
                    KeyConditionExpression = "tagGroup = :group",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":group"] = new AttributeValue { S = tagGroup }
                    },
                    ExclusiveStartKey = lastKey,
                    Limit = _options.QueryPageSize
                }).ConfigureAwait(false);

                foreach (var item in response.Items)
                {
                    if (!item.ContainsKey("eventId"))
                        continue;
                    tags.Add(DynamoTag.FromAttributeValues(item));
                }

                lastKey = response.LastEvaluatedKey;
            } while (lastKey != null && lastKey.Count > 0);
        }
        else
        {
            do
            {
                var response = await _client.ScanAsync(new ScanRequest
                {
                    TableName = _context.TagsTableName,
                    ExclusiveStartKey = lastKey,
                    Limit = _options.QueryPageSize
                }).ConfigureAwait(false);

                foreach (var item in response.Items)
                {
                    if (!item.ContainsKey("eventId"))
                        continue;
                    tags.Add(DynamoTag.FromAttributeValues(item));
                }

                lastKey = response.LastEvaluatedKey;
            } while (lastKey != null && lastKey.Count > 0);
        }

        return tags;
    }

    private static Dictionary<string, AttributeValue> BuildEventKey(string eventId)
    {
        return new Dictionary<string, AttributeValue>
        {
            ["pk"] = new AttributeValue { S = $"EVENT#{eventId}" },
            ["sk"] = new AttributeValue { S = $"EVENT#{eventId}" }
        };
    }

    private IReadOnlyList<TagWriteResult> BuildTagWriteResults(IEnumerable<Event> events)
    {
        var now = DateTimeOffset.UtcNow;
        return events
            .SelectMany(e => e.Tags)
            .Distinct(StringComparer.Ordinal)
            .Select(tag => new TagWriteResult(tag, 1, now))
            .ToList();
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
                        $"Failed to deserialize event payload of type {eventType}. " +
                        "Make sure the event type is registered in DcbDomainTypes."));
            }

            return ResultBox.FromValue(payload);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IEventPayload>(ex);
        }
    }

    private string GetGsiPartitionKey(string sortableUniqueId)
    {
        if (_options.WriteShardCount <= 1)
            return DynamoDbContext.EventsGsiPartitionKey;

        var shard = ComputeShardIndex(sortableUniqueId, _options.WriteShardCount);
        return $"{DynamoDbContext.EventsGsiPartitionKey}#{shard}";
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

    private sealed record EventWriteItem(Event Event, DynamoEvent DynamoEvent, List<DynamoTag> DynamoTags);
}
