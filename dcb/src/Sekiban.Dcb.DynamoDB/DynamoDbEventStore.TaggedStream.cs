using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.DynamoDB.Models;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text;

namespace Sekiban.Dcb.DynamoDB;

public partial class DynamoDbEventStore
{
    private const string TaggedStreamHighSortKeySuffix = "\uffff";

    /// <summary>Declares the callback-native tag stream used by cold rebuilds.</summary>
    public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
        TaggedStreamCapabilityDescriptor.Native("DynamoDB");

    /// <summary>
    ///     Streams tag references in Dynamo's sort-key order, retaining only one query page and one BatchGet chunk at a
    ///     time. BatchGetItem has no response order contract, so every chunk is explicitly rejoined to the already
    ///     ordered query references before callbacks run.
    /// </summary>
    public async Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
        ITag tag,
        SortableUniqueId? since,
        SortableUniqueId? until,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(onEvent);

        var queryPages = 0;
        var batchGetChunks = 0;
        var peakPageReferences = 0;
        var peakChunkBodies = 0;
        var consumedCapacity = 0d;
        var emitted = 0;
        string? lastSortableUniqueId = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageSize = ValidateTaggedStreamPageSize();
            var batchSize = ValidateTaggedStreamBatchSize();
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var tagPartitionKey = BuildTagPk(serviceId, tag.GetTag());
            Dictionary<string, AttributeValue>? lastEvaluatedKey = null;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var query = BuildTaggedStreamQuery(tagPartitionKey, since, until, pageSize, lastEvaluatedKey);
                var page = await _client.QueryAsync(query, cancellationToken).ConfigureAwait(false);
                queryPages++;
                consumedCapacity += page.ConsumedCapacity?.CapacityUnits ?? 0d;

                // This is the one bounded reference page. Nothing from a prior page survives its loop iteration.
                var pageReferences = page.Items
                    .Where(item => item.ContainsKey("eventId") && item.ContainsKey("sortableUniqueId"))
                    .Select(item => new TaggedStreamReference(
                        item["eventId"].S,
                        item["sortableUniqueId"].S))
                    .ToList();
                peakPageReferences = Math.Max(peakPageReferences, pageReferences.Count);

                for (var start = 0; start < pageReferences.Count; start += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // This is the one bounded event body chunk. The backing page remains references only.
                    var chunk = pageReferences.GetRange(start, Math.Min(batchSize, pageReferences.Count - start));
                    var read = BuildTaggedStreamBatchGetRequest(serviceId, chunk);
                    var eventsById = await ExecuteTaggedStreamBatchGetWithRetryAsync(
                        read,
                        capacity => consumedCapacity += capacity,
                        cancellationToken).ConfigureAwait(false);
                    batchGetChunks++;
                    peakChunkBodies = Math.Max(peakChunkBodies, eventsById.Count);

                    // The query order, not BatchGet's arbitrary response order, is the public callback order.
                    foreach (var reference in chunk)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!eventsById.TryGetValue(reference.EventId, out var dynamoEvent))
                        {
                            throw new InvalidOperationException(
                                $"Event not found for tag entry: {reference.EventId}");
                        }

                        if (!string.Equals(dynamoEvent.ServiceId, serviceId, StringComparison.Ordinal))
                        {
                            throw new UnauthorizedAccessException(
                                $"Event {reference.EventId} does not belong to service {serviceId}.");
                        }

                        if (!string.Equals(
                                dynamoEvent.SortableUniqueId,
                                reference.SortableUniqueId,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Event {reference.EventId} does not match its ordered tag reference.");
                        }

                        var serializableEvent = ToSerializableEvent(dynamoEvent);
                        await onEvent(serializableEvent).ConfigureAwait(false);
                        emitted++;
                        lastSortableUniqueId = serializableEvent.SortableUniqueIdValue;
                    }
                }

                _options.ReadProgressCallback?.Invoke(emitted, consumedCapacity);
                lastEvaluatedKey = page.LastEvaluatedKey;
            } while (lastEvaluatedKey is { Count: > 0 });

            return ResultBox.FromValue(new SerializableEventStreamReadResult(emitted, lastSortableUniqueId));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                LogReadFailed(_logger, ex.Message, ex);
            }

            return ResultBox.Error<SerializableEventStreamReadResult>(ex);
        }
        finally
        {
            var telemetry = new DynamoDbTaggedStreamTelemetry(
                queryPages,
                batchGetChunks,
                peakPageReferences,
                peakChunkBodies,
                consumedCapacity);
            try
            {
                _options.TaggedStreamTelemetryCallback?.Invoke(telemetry);
            }
            catch
            {
                // Observability is deliberately unable to alter a data-path result.
            }
        }
    }

    private QueryRequest BuildTaggedStreamQuery(
        string tagPartitionKey,
        SortableUniqueId? since,
        SortableUniqueId? until,
        int pageSize,
        Dictionary<string, AttributeValue>? lastEvaluatedKey)
    {
        var values = new Dictionary<string, AttributeValue>
        {
            [":pk"] = new() { S = tagPartitionKey }
        };
        string keyCondition;
        if (since is not null && until is not null)
        {
            // A real tag row's suffix begins with '#', which sorts before the high sentinel. Thus this inclusive
            // BETWEEN lower bound is exactly exclusive-after <paramref name="since" /> while remaining valid Dynamo
            // key-condition syntax; the upper sentinel makes <paramref name="until" /> inclusive for every event id.
            keyCondition = "pk = :pk AND sk BETWEEN :since AND :until";
            values[":since"] = new AttributeValue { S = ToExclusiveAfterSortKey(since.Value) };
            values[":until"] = new AttributeValue { S = ToInclusiveUntilSortKey(until.Value) };
        }
        else if (since is not null)
        {
            keyCondition = "pk = :pk AND sk > :since";
            values[":since"] = new AttributeValue { S = ToExclusiveAfterSortKey(since.Value) };
        }
        else if (until is not null)
        {
            keyCondition = "pk = :pk AND sk <= :until";
            values[":until"] = new AttributeValue { S = ToInclusiveUntilSortKey(until.Value) };
        }
        else
        {
            keyCondition = "pk = :pk";
        }

        return new QueryRequest
        {
            TableName = _context.TagsTableName,
            KeyConditionExpression = keyCondition,
            ExpressionAttributeValues = values,
            ScanIndexForward = true,
            Limit = pageSize,
            ExclusiveStartKey = lastEvaluatedKey,
            ConsistentRead = _options.UseConsistentReads,
            ReturnConsumedCapacity = ReturnConsumedCapacity.TOTAL
        };
    }

    private BatchGetItemRequest BuildTaggedStreamBatchGetRequest(
        string serviceId,
        IReadOnlyList<TaggedStreamReference> references)
    {
        return new BatchGetItemRequest
        {
            RequestItems = new Dictionary<string, KeysAndAttributes>
            {
                [_context.EventsTableName] = new KeysAndAttributes
                {
                    Keys = references.Select(reference => BuildEventKey(serviceId, reference.EventId)).ToList(),
                    ConsistentRead = _options.UseConsistentReads
                }
            },
            ReturnConsumedCapacity = ReturnConsumedCapacity.TOTAL
        };
    }

    private async Task<Dictionary<string, DynamoEvent>> ExecuteTaggedStreamBatchGetWithRetryAsync(
        BatchGetItemRequest request,
        Action<double> recordCapacity,
        CancellationToken cancellationToken)
    {
        var eventsById = new Dictionary<string, DynamoEvent>(StringComparer.Ordinal);
        var pending = request.RequestItems;

        for (var attempt = 0; attempt <= _options.MaxRetryAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await _client.BatchGetItemAsync(
                new BatchGetItemRequest
                {
                    RequestItems = pending,
                    ReturnConsumedCapacity = ReturnConsumedCapacity.TOTAL
                },
                cancellationToken).ConfigureAwait(false);
            recordCapacity(response.ConsumedCapacity?.Sum(capacity => capacity.CapacityUnits ?? 0d) ?? 0d);

            if (response.Responses.TryGetValue(_context.EventsTableName, out var items))
            {
                foreach (var item in items)
                {
                    var dynamoEvent = DynamoEvent.FromAttributeValues(item);
                    eventsById[dynamoEvent.EventId] = dynamoEvent;
                }
            }

            if (response.UnprocessedKeys is not { Count: > 0 })
            {
                return eventsById;
            }

            pending = response.UnprocessedKeys;
            await Task.Delay(ComputeBackoff(attempt), cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("BatchGetItem exceeded retry limit.");
    }

    private int ValidateTaggedStreamPageSize()
    {
        if (_options.QueryPageSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(_options.QueryPageSize),
                _options.QueryPageSize,
                "Native tagged streaming requires QueryPageSize to be at least one.");
        }

        return _options.QueryPageSize;
    }

    private int ValidateTaggedStreamBatchSize()
    {
        if (_options.MaxBatchGetItems is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(_options.MaxBatchGetItems),
                _options.MaxBatchGetItems,
                "Native tagged streaming requires MaxBatchGetItems to be between 1 and 100.");
        }

        return _options.MaxBatchGetItems;
    }

    private static string ToExclusiveAfterSortKey(string sortableUniqueId) =>
        $"{sortableUniqueId}{TaggedStreamHighSortKeySuffix}";

    private static string ToInclusiveUntilSortKey(string sortableUniqueId) =>
        $"{sortableUniqueId}{TaggedStreamHighSortKeySuffix}";

    private static SerializableEvent ToSerializableEvent(DynamoEvent dynamoEvent) =>
        new(
            Encoding.UTF8.GetBytes(dynamoEvent.Payload),
            dynamoEvent.SortableUniqueId,
            Guid.Parse(dynamoEvent.EventId),
            new EventMetadata(
                dynamoEvent.CausationId ?? string.Empty,
                dynamoEvent.CorrelationId ?? string.Empty,
                dynamoEvent.ExecutedUser ?? string.Empty),
            dynamoEvent.Tags.ToList(),
            dynamoEvent.EventType);

    private sealed record TaggedStreamReference(string EventId, string SortableUniqueId);
}
