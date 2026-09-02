using Microsoft.Azure.Cosmos;
using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Net;
using System.Text;

namespace Sekiban.Dcb.CosmosDb;

public partial class CosmosDbEventStore
{
    /// <summary>
    ///     Declares that this store has a callback-native tagged stream. The legacy list member remains intentionally
    ///     unchanged; callers that need bounded cold rebuilds resolve this capability instead of wrapping that list.
    /// </summary>
    public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
        TaggedStreamCapabilityDescriptor.Native("CosmosDb");

    /// <summary>
    ///     Streams one tag-index query page at a time and keeps a bounded, ordinal queue of event point reads. Reads may
    ///     complete out of order, but only the queue head is published, so a slow or failed head cannot let a later event
    ///     escape. This deliberately does not share the legacy list implementation, which materializes the complete
    ///     identifier history before issuing its reads.
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

        var options = _context.Options;
        var telemetryGate = new object();
        var indexPages = 0;
        var pointReads = 0;
        var peakInFlightReads = 0;
        var throttledRequests = 0;
        var requestCharge = 0d;
        var emitted = 0;
        string? lastSortableUniqueId = null;
        var pendingReads = new Queue<TaggedStreamPointRead>();
        using var issuedReadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        void RecordResponse(double charge)
        {
            lock (telemetryGate)
            {
                requestCharge += charge;
            }
        }

        void RecordThrottle(double charge)
        {
            lock (telemetryGate)
            {
                requestCharge += charge;
                throttledRequests++;
            }
        }

        async Task ObserveOutstandingReadsAsync()
        {
            await issuedReadCancellation.CancelAsync().ConfigureAwait(false);
            while (pendingReads.TryDequeue(out var pendingRead))
            {
                try
                {
                    await pendingRead.Completion.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The linked token intentionally cancels speculative reads after the ordinal head failed/cancelled.
                }
                catch
                {
                    // The head failure is the result surfaced to the caller. Observe tail failures before returning.
                }
            }
        }

        async Task EmitHeadAsync()
        {
            var pendingRead = pendingReads.Dequeue();
            var serializableEvent = await pendingRead.Completion.ConfigureAwait(false);
            if (serializableEvent is null)
            {
                return; // A tag row whose event was deleted is treated the same as the compatible list reader.
            }

            cancellationToken.ThrowIfCancellationRequested();
            await onEvent(serializableEvent).ConfigureAwait(false);
            emitted++;
            lastSortableUniqueId = serializableEvent.SortableUniqueIdValue;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageSize = options.GetTaggedStreamIndexPageSize();
            var window = options.ValidateTaggedStreamPointReadWindow();
            var serviceId = CurrentServiceId;
            var tagsSettings = _containerResolver.ResolveTagsContainer(serviceId);
            var eventsSettings = _containerResolver.ResolveEventsContainer(serviceId);
            var tagsContainer = await _context.GetTagsContainerAsync(tagsSettings).ConfigureAwait(false);
            var eventsContainer = await _context.GetEventsContainerAsync(eventsSettings).ConfigureAwait(false);
            var (queryDefinition, requestOptions) = BuildTaggedStreamIndexQuery(
                tag.GetTag(), serviceId, since, until, pageSize);

            using var iterator = tagsContainer.GetItemQueryIterator<dynamic>(queryDefinition, requestOptions: requestOptions);
            while (iterator.HasMoreResults)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FeedResponse<dynamic> page;
                try
                {
                    page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    RecordThrottle(ex.RequestCharge);
                    throw;
                }
                lock (telemetryGate)
                {
                    indexPages++;
                    requestCharge += page.RequestCharge;
                }

                foreach (var row in page)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    while (pendingReads.Count >= window)
                    {
                        await EmitHeadAsync().ConfigureAwait(false);
                    }

                    var eventId = (string)row.eventId;
                    Interlocked.Increment(ref pointReads);
                    pendingReads.Enqueue(new TaggedStreamPointRead(ReadEventPointAsync(
                        eventsContainer,
                        serviceId,
                        eventId,
                        RecordResponse,
                        RecordThrottle,
                        issuedReadCancellation.Token)));
                    peakInFlightReads = Math.Max(peakInFlightReads, pendingReads.Count);
                }
            }

            while (pendingReads.Count > 0)
            {
                var pendingRead = pendingReads.Dequeue();
                var serializableEvent = await pendingRead.Completion.ConfigureAwait(false);
                if (serializableEvent is null)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await onEvent(serializableEvent).ConfigureAwait(false);
                emitted++;
                lastSortableUniqueId = serializableEvent.SortableUniqueIdValue;
            }

            return ResultBox.FromValue(new SerializableEventStreamReadResult(emitted, lastSortableUniqueId));
        }
        catch (OperationCanceledException)
        {
            await ObserveOutstandingReadsAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await ObserveOutstandingReadsAsync().ConfigureAwait(false);
            return ResultBox.Error<SerializableEventStreamReadResult>(ex);
        }
        finally
        {
            CosmosTaggedStreamTelemetry telemetry;
            lock (telemetryGate)
            {
                telemetry = new CosmosTaggedStreamTelemetry(
                    indexPages,
                    pointReads,
                    peakInFlightReads,
                    requestCharge,
                    throttledRequests);
            }

            CosmosDbTelemetry.RecordTaggedStream(telemetry);
            try
            {
                options.TaggedStreamTelemetryCallback?.Invoke(telemetry);
            }
            catch
            {
                // Instrumentation must not change stream correctness or turn a completed callback into a failed rebuild.
            }
        }
    }

    private static (QueryDefinition QueryDefinition, QueryRequestOptions RequestOptions) BuildTaggedStreamIndexQuery(
        string tagString,
        string serviceId,
        SortableUniqueId? since,
        SortableUniqueId? until,
        int pageSize)
    {
        var tagPartitionKey = GetTagPartitionKey(tagString, serviceId);
        var where = "c.pk = @pk";
        if (since is not null)
        {
            where += " AND c.sortableUniqueId > @since";
        }

        if (until is not null)
        {
            where += " AND c.sortableUniqueId <= @until";
        }

        var query = new QueryDefinition($"SELECT c.eventId FROM c WHERE {where} ORDER BY c.sortableUniqueId")
            .WithParameter("@pk", tagPartitionKey);
        if (since is not null)
        {
            query = query.WithParameter("@since", since.Value);
        }

        if (until is not null)
        {
            query = query.WithParameter("@until", until.Value);
        }

        return (
            query,
            new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(tagPartitionKey),
                MaxItemCount = pageSize
            });
    }

    private static async Task<SerializableEvent?> ReadEventPointAsync(
        Container eventsContainer,
        string serviceId,
        string eventId,
        Action<double> recordResponse,
        Action<double> recordThrottle,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await eventsContainer.ReadItemAsync<CosmosEvent>(
                eventId,
                new PartitionKey(GetEventPartitionKey(eventId, serviceId)),
                requestOptions: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            recordResponse(response.RequestCharge);

            var cosmosEvent = response.Resource;
            if (!ServiceIdMatches(cosmosEvent.ServiceId, serviceId))
            {
                throw new UnauthorizedAccessException($"Event {eventId} does not belong to service {serviceId}.");
            }

            return new SerializableEvent(
                Encoding.UTF8.GetBytes(cosmosEvent.Payload),
                cosmosEvent.SortableUniqueId,
                Guid.Parse(cosmosEvent.Id),
                new EventMetadata(
                    cosmosEvent.CausationId ?? string.Empty,
                    cosmosEvent.CorrelationId ?? string.Empty,
                    cosmosEvent.ExecutedUser ?? string.Empty),
                cosmosEvent.Tags?.ToList() ?? [],
                cosmosEvent.EventType);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            recordResponse(ex.RequestCharge);
            return null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            recordThrottle(ex.RequestCharge);
            throw;
        }
        catch (CosmosException ex)
        {
            recordResponse(ex.RequestCharge);
            throw;
        }
    }

    private sealed record TaggedStreamPointRead(Task<SerializableEvent?> Completion);
}
