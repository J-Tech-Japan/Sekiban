using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Dcb.Domain;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.DynamoDB.Models;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Tests.Cosmos;
using System.Text;
using System.Text.Json;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     SEK-G54 provider-real native tagged-stream matrix. Cosmos uses the existing container-shaped in-process emulator;
///     DynamoDB uses a request-shaped harness that evaluates the real request's key condition and returns BatchGet rows
///     in deliberately hostile order. These tests live in WithResult.Tests so both net9/net10 DCB PR jobs execute them.
/// </summary>
public sealed class RemoteTaggedStreamTests
{
    private const string ServiceId = "g54-service";
    private const string EventsTable = "g54-events";
    private const string TagsTable = "g54-tags";
    private static readonly DateTime BaseTime = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string P0 = SortableUniqueId.Generate(BaseTime.AddSeconds(-1), Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly string P1 = SortableUniqueId.Generate(BaseTime, Guid.Parse("00000000-0000-0000-0000-000000000002"));
    private static readonly string P2 = SortableUniqueId.Generate(BaseTime.AddSeconds(1), Guid.Parse("00000000-0000-0000-0000-000000000003"));
    private static readonly string P3 = SortableUniqueId.Generate(BaseTime.AddSeconds(2), Guid.Parse("00000000-0000-0000-0000-000000000004"));
    private static readonly string P4 = SortableUniqueId.Generate(BaseTime.AddSeconds(3), Guid.Parse("00000000-0000-0000-0000-000000000005"));
    private static readonly JsonSerializerOptions PayloadJsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task CosmosAndDynamoNativeStreams_KeepStreamingColdListColdAndCachedIncrementalParity()
    {
        var domain = BuildParityDomain();

        var cosmosOptions = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "g54-parity-events",
            TagsContainerName = "g54-parity-tags",
            MaxItemCountPerPage = 1,
            MaxConcurrentTaggedStreamPointReads = 1
        };
        var (cosmos, _) = NewCosmosStore(cosmosOptions);
        var cosmosTag = new RemoteParityTag("cosmos");
        Assert.True((await cosmos.WriteSerializableEventsAsync(ParityEvents(domain, cosmosTag))).IsSuccess);
        var cosmosResult = await RunParityRoutesAsync(cosmos, "CosmosDb", domain, cosmosTag);

        // Keep the provider's arbitrary BatchGet order hostile on the actor route too. If the production rejoin is
        // removed, the G53 consumer guard sees P2 before P1 and the rebuild fails before it can publish state.
        var dynamoClient = new NativeTaggedStreamDynamoClient { ReverseBatchResponses = true };
        var dynamoOptions = new DynamoDbEventStoreOptions
        {
            AutoCreateTables = false,
            EventsTableName = EventsTable,
            TagsTableName = TagsTable,
            QueryPageSize = 1,
            MaxBatchGetItems = 1
        };
        var dynamo = NewDynamoStore(dynamoClient, dynamoOptions, domain);
        var dynamoTag = new RemoteParityTag("dynamo");
        SeedDynamo(dynamoClient, dynamoTag, ParityEvents(domain, dynamoTag));
        var dynamoResult = await RunParityRoutesAsync(dynamo, "DynamoDB", domain, dynamoTag);

        AssertEquivalent(cosmosResult.StreamingCold, dynamoResult.StreamingCold, "Cosmos / Dynamo streaming cold");
    }

    [Fact]
    public async Task CosmosNativeTaggedStream_FivePointBounds_PreservesPayloadVersionLastSuid_AndReportsBoundedTelemetry()
    {
        var telemetry = new List<CosmosTaggedStreamTelemetry>();
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "g54-events",
            TagsContainerName = "g54-tags",
            MaxItemCountPerPage = 1,
            MaxConcurrentTaggedStreamPointReads = 1,
            TaggedStreamTelemetryCallback = telemetry.Add
        };
        var (store, client) = NewCosmosStore(options);
        var tag = new NativeTag("cosmos-bounds");
        var fixture = FivePointFixture(tag);
        Assert.True((await store.WriteSerializableEventsAsync(fixture)).IsSuccess);

        var emitted = new List<SerializableEvent>();
        var stream = await store.StreamSerializableEventsByTagAsync(
            tag,
            new SortableUniqueId(P1),
            new SortableUniqueId(P3),
            @event =>
            {
                emitted.Add(@event);
                return ValueTask.CompletedTask;
            },
            new CancellationTokenSource().Token);

        Assert.True(stream.IsSuccess, stream.IsSuccess ? string.Empty : stream.GetException().ToString());
        Assert.Equal(new[] { P2, P3 }, emitted.Select(@event => @event.SortableUniqueIdValue));
        Assert.All(emitted, @event => Assert.Equal("RemoteTaggedV2", @event.EventPayloadName));
        Assert.All(emitted, @event => Assert.Equal(Encoding.UTF8.GetBytes("{\"v\":2}"), @event.Payload));
        Assert.Equal(2, stream.GetValue().EventsRead);
        Assert.Equal(P3, stream.GetValue().LastSortableUniqueId);
        Assert.Equal(2, client.Container(options.EventsContainerName).PointReads);
        Assert.Equal(1, client.Container(options.EventsContainerName).MaximumInFlightPointReads);
        Assert.All(client.Container(options.TagsContainerName).QueryReadTokens, token => Assert.True(token.CanBeCanceled));
        var observed = Assert.Single(telemetry);
        Assert.Equal(2, observed.IndexPages);
        Assert.Equal(2, observed.PointReads);
        Assert.Equal(1, observed.PeakInFlightPointReads);
        Assert.True(observed.RequestCharge > 0);
        Assert.Equal(0, observed.ThrottledRequests);

        var resolution = SekibanDcbCapabilityResolver.ResolveTaggedStream(store, "Cosmos");
        Assert.True(resolution.IsSupported);
        Assert.True(resolution.Descriptor.NativeStreaming);
        Assert.Equal("CosmosDb", resolution.Descriptor.ProviderName);
    }

    [Fact]
    public async Task CosmosNativeTaggedStream_CompletionOrderMutant_IsKilledByHeadOrderedPublication()
    {
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "g54-events",
            TagsContainerName = "g54-tags",
            MaxItemCountPerPage = 2,
            MaxConcurrentTaggedStreamPointReads = 2
        };
        var (store, client) = NewCosmosStore(options);
        var tag = new NativeTag("cosmos-completion-order");
        var fixture = FivePointFixture(tag);
        Assert.True((await store.WriteSerializableEventsAsync(fixture)).IsSuccess);

        var firstEventId = fixture.Single(@event => @event.SortableUniqueIdValue == P2).Id.ToString();
        var secondEventId = fixture.Single(@event => @event.SortableUniqueIdValue == P3).Id.ToString();
        var firstStarted = NewSignal();
        var secondFinished = NewSignal();
        var releaseFirst = NewSignal();
        var events = client.Container(options.EventsContainerName);
        events.ReadItemGateById = async (id, cancellationToken) =>
        {
            if (id == firstEventId)
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (id == secondEventId)
            {
                secondFinished.TrySetResult();
            }
        };

        var emitted = new List<string>();
        var streaming = store.StreamSerializableEventsByTagAsync(
            tag,
            new SortableUniqueId(P1),
            new SortableUniqueId(P3),
            @event =>
            {
                emitted.Add(@event.SortableUniqueIdValue);
                return ValueTask.CompletedTask;
            });

        try
        {
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await secondFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(emitted); // P3 completed first, but the P2 head is not released yet.
        }
        finally
        {
            releaseFirst.TrySetResult();
        }

        var result = await streaming;
        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(new[] { P2, P3 }, emitted);
        Assert.Equal(options.MaxConcurrentTaggedStreamPointReads, events.MaximumInFlightPointReads);
    }

    [Fact]
    public async Task CosmosNativeTaggedStream_FailedHeadCancelsQueuedReadsAndPublishesNothingAfterTheHead()
    {
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "g54-events",
            TagsContainerName = "g54-tags",
            MaxItemCountPerPage = 2,
            MaxConcurrentTaggedStreamPointReads = 2
        };
        var (store, client) = NewCosmosStore(options);
        var tag = new NativeTag("cosmos-failed-head");
        var fixture = FivePointFixture(tag);
        Assert.True((await store.WriteSerializableEventsAsync(fixture)).IsSuccess);
        var events = client.Container(options.EventsContainerName);
        var tailStarted = NewSignal();
        var p3EventId = fixture.Single(@event => @event.SortableUniqueIdValue == P3).Id.ToString();
        events.ReadItemGateById = async (id, cancellationToken) =>
        {
            if (id == p3EventId)
            {
                tailStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
        };
        events.ReadFaults.Enqueue(new InvalidOperationException("failed P2 head"));

        var emitted = new List<string>();
        var result = await store.StreamSerializableEventsByTagAsync(
            tag,
            new SortableUniqueId(P1),
            new SortableUniqueId(P3),
            @event =>
            {
                emitted.Add(@event.SortableUniqueIdValue);
                return ValueTask.CompletedTask;
            });

        Assert.False(result.IsSuccess);
        Assert.Equal("failed P2 head", result.GetException().Message);
        Assert.Empty(emitted);
        await tailStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, events.PointReads);
        Assert.All(events.PointReadTokens, token => Assert.True(token.IsCancellationRequested));
    }

    [Fact]
    public async Task CosmosNativeTaggedStream_CancellationStopsNewPointReads_AndForwardsProviderTokens()
    {
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "g54-events",
            TagsContainerName = "g54-tags",
            MaxItemCountPerPage = 2,
            MaxConcurrentTaggedStreamPointReads = 2
        };
        var (store, client) = NewCosmosStore(options);
        var tag = new NativeTag("cosmos-cancel");
        Assert.True((await store.WriteSerializableEventsAsync(FivePointFixture(tag))).IsSuccess);
        var events = client.Container(options.EventsContainerName);
        var readStarted = NewSignal();
        events.ReadItemGate = async cancellationToken =>
        {
            readStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        };

        using var cancellation = new CancellationTokenSource();
        var streaming = store.StreamSerializableEventsByTagAsync(
            tag,
            new SortableUniqueId(P1),
            new SortableUniqueId(P3),
            _ => ValueTask.CompletedTask,
            cancellation.Token);
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var issuedBeforeCancellation = events.PointReads;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => streaming);
        Assert.Equal(issuedBeforeCancellation, events.PointReads);
        Assert.InRange(events.PointReads, 1, options.MaxConcurrentTaggedStreamPointReads);
        Assert.InRange(events.MaximumInFlightPointReads, 1, options.MaxConcurrentTaggedStreamPointReads);
        Assert.All(events.PointReadTokens, token => Assert.True(token.CanBeCanceled && token.IsCancellationRequested));
        Assert.All(
            client.Container(options.TagsContainerName).QueryReadTokens,
            token => Assert.True(token.CanBeCanceled && token.IsCancellationRequested));
    }

    [Fact]
    public async Task CosmosNativeTaggedStream_CapturedHeadExcludesAnAppendAboveTheHeadDuringAReleasedCallback()
    {
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "g54-events",
            TagsContainerName = "g54-tags",
            MaxItemCountPerPage = 1,
            MaxConcurrentTaggedStreamPointReads = 1
        };
        var (store, _) = NewCosmosStore(options);
        var tag = new NativeTag("cosmos-captured-head");
        var initial = FivePointFixture(tag).Where(@event => @event.SortableUniqueIdValue != P4).ToList();
        Assert.True((await store.WriteSerializableEventsAsync(initial)).IsSuccess);
        var capturedHead = await store.GetLatestSortableUniqueIdAsync();
        Assert.True(capturedHead.IsSuccess, capturedHead.IsSuccess ? string.Empty : capturedHead.GetException().ToString());
        Assert.Equal(P3, capturedHead.GetValue());

        var firstCallback = NewSignal();
        var releaseCallback = NewSignal();
        var emitted = new List<string>();
        var stream = store.StreamSerializableEventsByTagAsync(
            tag,
            new SortableUniqueId(P1),
            new SortableUniqueId(capturedHead.GetValue()),
            async @event =>
            {
                emitted.Add(@event.SortableUniqueIdValue);
                if (emitted.Count == 1)
                {
                    firstCallback.TrySetResult();
                    await releaseCallback.Task;
                }
            });

        try
        {
            await firstCallback.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var appendAboveHead = FivePointFixture(tag).Single(@event => @event.SortableUniqueIdValue == P4);
            Assert.True((await store.WriteSerializableEventsAsync([appendAboveHead])).IsSuccess);
        }
        finally
        {
            releaseCallback.TrySetResult();
        }

        var result = await stream;
        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(new[] { P2, P3 }, emitted);
        Assert.DoesNotContain(P4, emitted);
    }

    [Fact]
    public async Task CosmosNativeTaggedStream_ThrottledPointReadReports429AndRequestChargeTelemetry()
    {
        var telemetry = new List<CosmosTaggedStreamTelemetry>();
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "g54-events",
            TagsContainerName = "g54-tags",
            MaxItemCountPerPage = 1,
            MaxConcurrentTaggedStreamPointReads = 1,
            TaggedStreamTelemetryCallback = telemetry.Add
        };
        var (store, client) = NewCosmosStore(options);
        var tag = new NativeTag("cosmos-telemetry");
        Assert.True((await store.WriteSerializableEventsAsync([FixtureEvent(P2, tag, 2)])).IsSuccess);
        client.Container(options.EventsContainerName).ReadFaults.Enqueue(CosmosFailures.Throttled(TimeSpan.FromMilliseconds(1)));

        var result = await store.StreamSerializableEventsByTagAsync(tag, null, null, _ => ValueTask.CompletedTask);

        Assert.False(result.IsSuccess);
        var observed = Assert.Single(telemetry);
        Assert.Equal(1, observed.ThrottledRequests);
        Assert.True(observed.RequestCharge > 0);
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(65, 1000)]
    [InlineData(9, 8)]
    public async Task CosmosNativeTaggedStream_InvalidWindowFailsBeforeItReadsTheTagIndex(
        int configuredWindow,
        int pageSize)
    {
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "g54-events",
            TagsContainerName = "g54-tags",
            MaxItemCountPerPage = pageSize,
            MaxConcurrentTaggedStreamPointReads = configuredWindow
        };
        var (store, client) = NewCosmosStore(options);

        var result = await store.StreamSerializableEventsByTagAsync(
            new NativeTag("cosmos-invalid-window"),
            null,
            null,
            _ => ValueTask.CompletedTask);

        Assert.False(result.IsSuccess);
        Assert.IsType<ArgumentOutOfRangeException>(result.GetException());
        Assert.Equal(0, client.Container(options.TagsContainerName).Queries);
        Assert.Equal(configuredWindow, options.MaxConcurrentTaggedStreamPointReads);
        Assert.Equal(CosmosDbEventStoreOptions.DefaultMaxConcurrentTaggedStreamPointReads,
            new CosmosDbEventStoreOptions().MaxConcurrentTaggedStreamPointReads);
    }

    [Fact]
    public async Task CosmosNativeTaggedStream_CompatibilityPresetKeepsItsLegacyListSentinelButUsesABoundedNativePage()
    {
        var options = CosmosDbEventStoreOptions.CreateForCompatibility();
        options.EventsContainerName = "g54-events";
        options.TagsContainerName = "g54-tags";
        var (store, _) = NewCosmosStore(options);

        var result = await store.StreamSerializableEventsByTagAsync(
            new NativeTag("cosmos-compatibility-page"),
            null,
            null,
            _ => ValueTask.CompletedTask);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(-1, options.MaxItemCountPerPage); // Existing list-reader compatibility behavior remains unchanged.
        Assert.Equal(CosmosDbEventStoreOptions.DefaultTaggedStreamIndexPageSize, options.GetTaggedStreamIndexPageSize());
    }

    [Fact]
    public async Task DynamoNativeTaggedStream_BoundsArePushedDown_AndBatchGetOrderMutantIsKilled()
    {
        var telemetry = new List<DynamoDbTaggedStreamTelemetry>();
        var progress = new List<(long EventsRead, double ConsumedCapacity)>();
        var client = new NativeTaggedStreamDynamoClient { ReverseBatchResponses = true };
        var options = new DynamoDbEventStoreOptions
        {
            AutoCreateTables = false,
            EventsTableName = EventsTable,
            TagsTableName = TagsTable,
            QueryPageSize = 2,
            MaxBatchGetItems = 2,
            TaggedStreamTelemetryCallback = telemetry.Add,
            ReadProgressCallback = (eventsRead, consumedCapacity) => progress.Add((eventsRead, consumedCapacity))
        };
        var store = NewDynamoStore(client, options);
        var tag = new NativeTag("dynamo-bounds");
        SeedDynamo(client, tag, FivePointFixture(tag));

        var emitted = new List<SerializableEvent>();
        using var tokenSource = new CancellationTokenSource();
        var result = await store.StreamSerializableEventsByTagAsync(
            tag,
            new SortableUniqueId(P1),
            new SortableUniqueId(P3),
            @event =>
            {
                emitted.Add(@event);
                return ValueTask.CompletedTask;
            },
            tokenSource.Token);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(new[] { P2, P3 }, emitted.Select(@event => @event.SortableUniqueIdValue));
        Assert.All(emitted, @event => Assert.Equal(Encoding.UTF8.GetBytes("{\"v\":2}"), @event.Payload));
        Assert.Equal(P3, result.GetValue().LastSortableUniqueId);
        var query = Assert.Single(client.Queries);
        Assert.Equal("pk = :pk AND sk BETWEEN :since AND :until", query.KeyConditionExpression);
        Assert.Equal(P1 + "\uffff", query.ExpressionAttributeValues[":since"].S);
        Assert.Equal(P3 + "\uffff", query.ExpressionAttributeValues[":until"].S);
        Assert.All(client.QueryTokens, token => Assert.True(token.CanBeCanceled));
        Assert.All(client.BatchGetTokens, token => Assert.True(token.CanBeCanceled));
        var observed = Assert.Single(telemetry);
        Assert.Equal(1, observed.QueryPages);
        Assert.Equal(1, observed.BatchGetChunks);
        Assert.Equal(2, observed.PeakPageReferences);
        Assert.Equal(2, observed.PeakChunkBodies);
        Assert.True(observed.ConsumedCapacity > 0);
        var existingProgress = Assert.Single(progress);
        Assert.Equal(2, existingProgress.EventsRead);
        Assert.True(existingProgress.ConsumedCapacity > 0);

        var resolution = SekibanDcbCapabilityResolver.ResolveTaggedStream(store, "DynamoDB");
        Assert.True(resolution.IsSupported);
        Assert.True(resolution.Descriptor.NativeStreaming);
        Assert.Equal("DynamoDB", resolution.Descriptor.ProviderName);
    }

    [Fact]
    public async Task DynamoNativeTaggedStream_RetainsOnlyOnePageAndOneChunk()
    {
        var telemetry = new List<DynamoDbTaggedStreamTelemetry>();
        var client = new NativeTaggedStreamDynamoClient();
        var options = new DynamoDbEventStoreOptions
        {
            AutoCreateTables = false,
            EventsTableName = EventsTable,
            TagsTableName = TagsTable,
            QueryPageSize = 3,
            MaxBatchGetItems = 2,
            TaggedStreamTelemetryCallback = telemetry.Add
        };
        var store = NewDynamoStore(client, options);
        var tag = new NativeTag("dynamo-bounds-memory");
        SeedDynamo(client, tag, FivePointFixture(tag));
        var emitted = new List<string>();

        var result = await store.StreamSerializableEventsByTagAsync(
            tag,
            null,
            null,
            @event =>
            {
                emitted.Add(@event.SortableUniqueIdValue);
                return ValueTask.CompletedTask;
            });

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(new[] { P0, P1, P2, P3, P4 }, emitted);
        var observed = Assert.Single(telemetry);
        Assert.Equal(2, observed.QueryPages);
        Assert.Equal(3, observed.BatchGetChunks);
        Assert.InRange(observed.PeakPageReferences, 1, options.QueryPageSize);
        Assert.InRange(observed.PeakChunkBodies, 1, options.MaxBatchGetItems);
        Assert.InRange(client.MaximumQueryRows, 1, options.QueryPageSize);
        Assert.InRange(client.MaximumBatchKeys, 1, options.MaxBatchGetItems);
    }

    [Fact]
    public async Task DynamoNativeTaggedStream_CancellationAfterTheFirstPagePreventsAnyBatchOrLaterPage()
    {
        var client = new NativeTaggedStreamDynamoClient();
        var options = new DynamoDbEventStoreOptions
        {
            AutoCreateTables = false,
            EventsTableName = EventsTable,
            TagsTableName = TagsTable,
            QueryPageSize = 1,
            MaxBatchGetItems = 1
        };
        var store = NewDynamoStore(client, options);
        var tag = new NativeTag("dynamo-cancel");
        SeedDynamo(client, tag, FivePointFixture(tag));
        using var cancellation = new CancellationTokenSource();
        client.AfterQuery = cancellation.Cancel;

        var streaming = store.StreamSerializableEventsByTagAsync(
            tag,
            null,
            null,
            _ => ValueTask.CompletedTask,
            cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => streaming);
        Assert.Single(client.Queries);
        Assert.All(client.QueryTokens, token => Assert.True(token.CanBeCanceled && token.IsCancellationRequested));
        Assert.Empty(client.BatchGetTokens);
    }

    [Fact]
    public async Task DynamoNativeTaggedStream_CapturedHeadExcludesAnAppendAboveTheHeadDuringAReleasedCallback()
    {
        var client = new NativeTaggedStreamDynamoClient();
        var options = new DynamoDbEventStoreOptions
        {
            AutoCreateTables = false,
            EventsTableName = EventsTable,
            TagsTableName = TagsTable,
            QueryPageSize = 1,
            MaxBatchGetItems = 1
        };
        var store = NewDynamoStore(client, options);
        var tag = new NativeTag("dynamo-captured-head");
        var initial = FivePointFixture(tag).Where(@event => @event.SortableUniqueIdValue != P4).ToList();
        SeedDynamo(client, tag, initial);
        var capturedHead = new SortableUniqueId(P3); // The caller captures this settled head before starting the stream.

        var firstCallback = NewSignal();
        var releaseCallback = NewSignal();
        var emitted = new List<string>();
        var stream = store.StreamSerializableEventsByTagAsync(
            tag,
            new SortableUniqueId(P1),
            capturedHead,
            async @event =>
            {
                emitted.Add(@event.SortableUniqueIdValue);
                if (emitted.Count == 1)
                {
                    firstCallback.TrySetResult();
                    await releaseCallback.Task;
                }
            });

        try
        {
            await firstCallback.Task.WaitAsync(TimeSpan.FromSeconds(10));
            SeedDynamo(client, tag, FivePointFixture(tag).Where(@event => @event.SortableUniqueIdValue == P4));
        }
        finally
        {
            releaseCallback.TrySetResult();
        }

        var result = await stream;
        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(new[] { P2, P3 }, emitted);
        Assert.DoesNotContain(P4, emitted);
    }

    [Fact]
    public async Task DynamoNativeTaggedStream_PreCancelledTokenDoesNotIssueAProviderRequest()
    {
        var client = new NativeTaggedStreamDynamoClient();
        var options = new DynamoDbEventStoreOptions
        {
            AutoCreateTables = false,
            EventsTableName = EventsTable,
            TagsTableName = TagsTable
        };
        var store = NewDynamoStore(client, options);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.StreamSerializableEventsByTagAsync(
            new NativeTag("dynamo-precancel"),
            null,
            null,
            _ => ValueTask.CompletedTask,
            cancellation.Token));
        Assert.Empty(client.Queries);
        Assert.Empty(client.BatchGetTokens);
    }

    [Fact]
    public void TaggedStreamInterfaceSignature_RemainsTheG53FrozenCapabilityContract()
    {
        var method = typeof(IStreamingTaggedSerializableEventStore).GetMethod(
            nameof(IStreamingTaggedSerializableEventStore.StreamSerializableEventsByTagAsync));
        Assert.NotNull(method);
        Assert.Equal(
            new[]
            {
                typeof(ITag),
                typeof(SortableUniqueId),
                typeof(SortableUniqueId),
                typeof(Func<SerializableEvent, ValueTask>),
                typeof(CancellationToken)
            },
            method!.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            typeof(Task<ResultBoxes.ResultBox<SerializableEventStreamReadResult>>),
            method.ReturnType);
    }

    [Fact]
    public void NativeRemoteTaggedStreamPaths_TripwireLegacyListDrainAndAllReferenceFanOut()
    {
        var root = FindRepositoryRoot();
        var cosmos = File.ReadAllText(Path.Combine(
            root,
            "dcb/src/Sekiban.Dcb.CosmosDb/CosmosDbEventStore.TaggedStream.cs"));
        var dynamo = File.ReadAllText(Path.Combine(
            root,
            "dcb/src/Sekiban.Dcb.DynamoDB/DynamoDbEventStore.TaggedStream.cs"));

        // These are direct regression tripwires: restoring the old whole-history helpers or a fan-out over every
        // reference must make the dedicated native paths fail before a cold-rebuild consumer can silently regress.
        Assert.DoesNotContain("FetchEventIdsAsync", cosmos, StringComparison.Ordinal);
        Assert.DoesNotContain("FetchSerializableEventsByIdsAsync", cosmos, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.WhenAll", cosmos, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadSerializableEventsByTagAsync", cosmos, StringComparison.Ordinal);
        Assert.DoesNotContain("QueryTagsAsync", dynamo, StringComparison.Ordinal);
        Assert.DoesNotContain("BatchGetEventsAsync", dynamo, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.WhenAll", dynamo, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadSerializableEventsByTagAsync", dynamo, StringComparison.Ordinal);
    }

    private static (CosmosDbEventStore Store, InMemoryCosmosClient Client) NewCosmosStore(CosmosDbEventStoreOptions options)
    {
        var client = new InMemoryCosmosClient();
        var context = new CosmosDbContext(client, "g54-db", options: options);
        return (
            new CosmosDbEventStore(
                context,
                DomainType.GetDomainTypes().EventTypes,
                new FixedServiceIdProvider(ServiceId),
                new DefaultCosmosContainerResolver(options)),
            client);
    }

    private static DynamoDbEventStore NewDynamoStore(
        NativeTaggedStreamDynamoClient client,
        DynamoDbEventStoreOptions options) =>
        NewDynamoStore(client, options, DomainType.GetDomainTypes());

    private static DynamoDbEventStore NewDynamoStore(
        NativeTaggedStreamDynamoClient client,
        DynamoDbEventStoreOptions options,
        DcbDomainTypes domain) =>
        new(new DynamoDbContext(client, Options.Create(options)), domain.EventTypes,
            new FixedServiceIdProvider(ServiceId));

    private static async Task<RemoteParityResult> RunParityRoutesAsync(
        IEventStore provider,
        string providerName,
        DcbDomainTypes domain,
        RemoteParityTag tag)
    {
        var streamingColdStore = new RemoteStreamingCountingStore(provider, providerName);
        var streamingCold = await ReadRemoteColdAsync(streamingColdStore, domain, tag, P2);
        Assert.Equal(1, streamingColdStore.StreamCalls);
        Assert.Equal(0, streamingColdStore.ListCalls);
        AssertRemoteParityExpected(streamingCold);

        var listColdStore = new RemoteListCountingStore(provider);
        var listCold = await ReadRemoteColdAsync(listColdStore, domain, tag, P2);
        Assert.Equal(0, listColdStore.StreamCalls);
        Assert.Equal(1, listColdStore.ListCalls);
        AssertRemoteParityExpected(listCold);

        var cachedIncrementalStore = new RemoteStreamingCountingStore(provider, providerName);
        var cachedIncremental = await ReadRemoteCachedIncrementalAsync(cachedIncrementalStore, domain, tag);
        Assert.Equal(2, cachedIncrementalStore.StreamCalls);
        Assert.Equal(0, cachedIncrementalStore.ListCalls);
        AssertRemoteParityExpected(cachedIncremental);

        // A skipped callback, version/last-id mutation, or a stream-to-list fallback changes one of these values.
        AssertEquivalent(streamingCold, listCold, $"{providerName} streaming / list cold");
        AssertEquivalent(streamingCold, cachedIncremental, $"{providerName} streaming / cached incremental");
        return new RemoteParityResult(streamingCold, listCold, cachedIncremental);
    }

    private static async Task<SerializableTagState> ReadRemoteColdAsync(
        IEventStore store,
        DcbDomainTypes domain,
        RemoteParityTag tag,
        string head)
    {
        var actor = CreateRemoteParityActor(
            store,
            domain,
            tag,
            new RemoteHeadActorAccessor(head),
            new InMemoryTagStatePersistent());
        return await actor.GetStateAsync();
    }

    private static async Task<SerializableTagState> ReadRemoteCachedIncrementalAsync(
        IEventStore store,
        DcbDomainTypes domain,
        RemoteParityTag tag)
    {
        var accessor = new RemoteHeadActorAccessor(P1);
        var actor = CreateRemoteParityActor(store, domain, tag, accessor, new InMemoryTagStatePersistent());
        var initial = await actor.GetStateAsync();
        Assert.Equal(1, initial.Version);
        Assert.Equal(P1, initial.LastSortedUniqueId);

        accessor.Head = P2;
        var incremental = await actor.GetStateAsync();
        var cacheHit = await actor.GetStateAsync();
        Assert.Equal(incremental.Payload, cacheHit.Payload);
        Assert.Equal(incremental.Version, cacheHit.Version);
        Assert.Equal(incremental.LastSortedUniqueId, cacheHit.LastSortedUniqueId);
        return incremental;
    }

    private static GeneralTagStateActor CreateRemoteParityActor(
        IEventStore store,
        DcbDomainTypes domain,
        RemoteParityTag tag,
        IActorObjectAccessor accessor,
        ITagStatePersistent persistent) =>
        new(
            $"{tag.GetTag()}:{RemoteParityProjector.ProjectorName}",
            store,
            domain.EventTypes,
            domain.TagProjectorTypes,
            domain.TagTypes,
            domain.TagStatePayloadTypes,
            new TagStateOptions(),
            accessor,
            persistent);

    private static IReadOnlyList<SerializableEvent> ParityEvents(DcbDomainTypes domain, RemoteParityTag tag) =>
    [
        new Event(
                new RemoteParityAdded(3),
                P1,
                nameof(RemoteParityAdded),
                Guid.Parse("00000000-0000-0000-0000-000000000201"),
                new EventMetadata("cause", "correlation", "parity"),
                [tag.GetTag()])
            .ToSerializableEvent(domain.EventTypes),
        new Event(
                new RemoteParityAdded(4),
                P2,
                nameof(RemoteParityAdded),
                Guid.Parse("00000000-0000-0000-0000-000000000202"),
                new EventMetadata("cause", "correlation", "parity"),
                [tag.GetTag()])
            .ToSerializableEvent(domain.EventTypes)
    ];

    private static DcbDomainTypes BuildParityDomain()
    {
        var events = new SimpleEventTypes();
        events.RegisterEventType<RemoteParityAdded>();
        var tagProjectors = new SimpleTagProjectorTypes();
        tagProjectors.RegisterProjector<RemoteParityProjector>();
        var payloads = new SimpleTagStatePayloadTypes();
        payloads.RegisterPayloadType<RemoteParityState>();
        return new DcbDomainTypes(
            events,
            new SimpleTagTypes(),
            tagProjectors,
            payloads,
            new SimpleMultiProjectorTypes(),
            new SimpleQueryTypes(),
            new JsonSerializerOptions());
    }

    private static void AssertRemoteParityExpected(SerializableTagState state)
    {
        Assert.Equal(2, state.Version);
        Assert.Equal(P2, state.LastSortedUniqueId);
        var payload = JsonSerializer.Deserialize<RemoteParityState>(state.Payload, PayloadJsonOptions);
        Assert.NotNull(payload);
        Assert.Equal(7, payload.Total);
    }

    private static void AssertEquivalent(SerializableTagState expected, SerializableTagState actual, string route)
    {
        Assert.True(expected.Payload.SequenceEqual(actual.Payload), $"Payload differed for {route}.");
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.LastSortedUniqueId, actual.LastSortedUniqueId);
    }

    private static IReadOnlyList<SerializableEvent> FivePointFixture(ITag tag) =>
    [
        FixtureEvent(P4, tag, 4),
        FixtureEvent(P2, tag, 2),
        FixtureEvent(P0, tag, 0),
        FixtureEvent(P3, tag, 3),
        FixtureEvent(P1, tag, 1)
    ];

    private static SerializableEvent FixtureEvent(string sortableUniqueId, ITag tag, int value) =>
        new(
            Encoding.UTF8.GetBytes("{\"v\":2}"),
            sortableUniqueId,
            Guid.Parse($"00000000-0000-0000-0000-{value + 101:D12}"),
            new EventMetadata("cause", "correlation", "user"),
            [tag.GetTag()],
            "RemoteTaggedV2");

    private static void SeedDynamo(
        NativeTaggedStreamDynamoClient client,
        ITag tag,
        IEnumerable<SerializableEvent> serializableEvents)
    {
        foreach (var serializableEvent in serializableEvents)
        {
            var source = new Event(
                new NativePayload(serializableEvent.SortableUniqueIdValue),
                serializableEvent.SortableUniqueIdValue,
                serializableEvent.EventPayloadName,
                serializableEvent.Id,
                serializableEvent.EventMetadata,
                serializableEvent.Tags);
            var dynamoEvent = DynamoEvent.FromEvent(
                source,
                Encoding.UTF8.GetString(serializableEvent.Payload),
                "unused-gsi",
                ServiceId);
            var dynamoTag = DynamoTag.FromEventTag(
                ServiceId,
                tag.GetTag(),
                $"{ServiceId}|{tag.GetTagGroup()}",
                serializableEvent.SortableUniqueIdValue,
                serializableEvent.Id,
                serializableEvent.EventPayloadName);
            client.Seed(EventsTable, dynamoEvent.ToAttributeValues());
            client.Seed(TagsTable, dynamoTag.ToAttributeValues());
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                Directory.Exists(Path.Combine(directory.FullName, "dcb")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Sekiban repository root for native stream tripwires.");
    }

    private sealed record RemoteParityResult(
        SerializableTagState StreamingCold,
        SerializableTagState ListCold,
        SerializableTagState CachedIncremental);

    private sealed record RemoteParityAdded(int Delta) : IEventPayload;

    private sealed record RemoteParityState(int Total) : ITagStatePayload;

    private sealed class RemoteParityProjector : ITagProjector<RemoteParityProjector>
    {
        public static string ProjectorVersion => "1";
        public static string ProjectorName => nameof(RemoteParityProjector);

        public static ITagStatePayload Project(ITagStatePayload current, Event @event)
        {
            var state = current as RemoteParityState ?? new RemoteParityState(0);
            return @event.Payload is RemoteParityAdded added
                ? state with { Total = state.Total + added.Delta }
                : state;
        }
    }

    private sealed class RemoteParityTag(string providerName) : ITag
    {
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "G54Parity";
        public string GetTagContent() => providerName;
        public string GetTag() => $"G54Parity:{providerName}";
    }

    private sealed class RemoteHeadActorAccessor(string head) : IActorObjectAccessor
    {
        private readonly RemoteHeadActor _actor = new();
        public string Head { get; set; } = head;

        public Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class
        {
            if (typeof(T) != typeof(ITagConsistentActorCommon))
            {
                return Task.FromResult(ResultBox.Error<T>(new NotSupportedException()));
            }

            _actor.Head = Head;
            return Task.FromResult(ResultBox.FromValue((T)(object)_actor));
        }

        public Task<bool> ActorExistsAsync(string actorId) => Task.FromResult(true);
    }

    private sealed class RemoteHeadActor : ITagConsistentActorCommon
    {
        public string Head { get; set; } = string.Empty;
        public Task<string> GetTagActorIdAsync() => Task.FromResult("G54Parity:head");
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => Task.FromResult(ResultBox.FromValue(Head));
        public Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string? lastSortableUniqueId) =>
            Task.FromResult(ResultBox.FromValue(new TagWriteReservation("reservation", DateTime.UtcNow.ToString("O"), "G54Parity:head")));
        public Task<bool> ConfirmReservationAsync(TagWriteReservation reservation) => Task.FromResult(true);
        public Task<bool> CancelReservationAsync(TagWriteReservation reservation) => Task.FromResult(true);
        public Task NotifyEventWrittenAsync() => Task.CompletedTask;
    }

    private abstract class RemoteCountingStore(IEventStore inner) : IEventStore
    {
        protected IEventStore Inner { get; } = inner;
        public int StreamCalls { get; protected set; }
        public int ListCalls { get; private set; }

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => Inner.ReadTagsAsync(tag);
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => Inner.GetLatestTagAsync(tag);
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => Inner.TagExistsAsync(tag);
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => Inner.GetEventCountAsync(since);
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => Inner.GetAllTagsAsync(tagGroup);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            Inner.ReadAllSerializableEventsAsync(since);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) =>
            Inner.ReadAllSerializableEventsAsync(since, maxCount);
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) => Inner.ReadSerializableEventAsync(eventId);
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => Inner.WriteSerializableEventsAsync(events);
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => Inner.GetLatestSortableUniqueIdAsync();

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null)
        {
            ListCalls++;
            return Inner.ReadSerializableEventsByTagAsync(tag, since);
        }
    }

    private sealed class RemoteListCountingStore(IEventStore inner) : RemoteCountingStore(inner);

    private sealed class RemoteStreamingCountingStore(IEventStore inner, string providerName) : RemoteCountingStore(inner),
        IStreamingTaggedSerializableEventStore, ITaggedStreamCapabilityProvider
    {
        public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
            TaggedStreamCapabilityDescriptor.Native($"{providerName} parity wrapper");

        public Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since,
            SortableUniqueId? until,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default)
        {
            StreamCalls++;
            return ((IStreamingTaggedSerializableEventStore)Inner).StreamSerializableEventsByTagAsync(
                tag,
                since,
                until,
                onEvent,
                cancellationToken);
        }
    }

    private sealed record NativePayload(string Value) : IEventPayload;

    private sealed class NativeTag(string content) : ITag
    {
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "Remote";
        public string GetTagContent() => content;
    }

    /// <summary>
    ///     In-process Dynamo request harness. It intentionally bases filtering on the exact production
    ///     <see cref="QueryRequest.KeyConditionExpression" /> and makes BatchGet responses reverse ordered on demand;
    ///     replacing the explicit reorder in production with response enumeration therefore fails deterministically.
    /// </summary>
    private sealed class NativeTaggedStreamDynamoClient : AmazonDynamoDBClient
    {
        private readonly Dictionary<(string Table, string Pk, string Sk), Dictionary<string, AttributeValue>> _items = new();
        private readonly object _gate = new();

        public NativeTaggedStreamDynamoClient()
            : base(
                new BasicAWSCredentials("g54", "g54"),
                new AmazonDynamoDBConfig { ServiceURL = "http://localhost:8000", AuthenticationRegion = "us-east-1" })
        {
        }

        public bool ReverseBatchResponses { get; set; }
        public Action? AfterQuery { get; set; }
        public List<QueryRequest> Queries { get; } = new();
        public List<CancellationToken> QueryTokens { get; } = new();
        public List<CancellationToken> BatchGetTokens { get; } = new();
        public int MaximumQueryRows { get; private set; }
        public int MaximumBatchKeys { get; private set; }

        public void Seed(string table, Dictionary<string, AttributeValue> item)
        {
            lock (_gate)
            {
                _items[(table, item["pk"].S, item["sk"].S)] = Clone(item);
            }
        }

        public override Task<QueryResponse> QueryAsync(
            QueryRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                Queries.Add(request);
                QueryTokens.Add(cancellationToken);
                var response = Query(request);
                AfterQuery?.Invoke();
                return Task.FromResult(response);
            }
        }

        public override Task<BatchGetItemResponse> BatchGetItemAsync(
            BatchGetItemRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                BatchGetTokens.Add(cancellationToken);
                var keys = request.RequestItems[EventsTable].Keys;
                MaximumBatchKeys = Math.Max(MaximumBatchKeys, keys.Count);
                var rows = keys
                    .Select(key => _items.TryGetValue((EventsTable, key["pk"].S, key["sk"].S), out var item)
                        ? Clone(item)
                        : null)
                    .Where(item => item is not null)
                    .Select(item => item!)
                    .ToList();
                if (ReverseBatchResponses)
                {
                    rows.Reverse();
                }

                return Task.FromResult(new BatchGetItemResponse
                {
                    Responses = new Dictionary<string, List<Dictionary<string, AttributeValue>>>
                    {
                        [EventsTable] = rows
                    },
                    UnprocessedKeys = new Dictionary<string, KeysAndAttributes>(),
                    ConsumedCapacity = [new ConsumedCapacity { CapacityUnits = 1d }]
                });
            }
        }

        private QueryResponse Query(QueryRequest request)
        {
            var values = request.ExpressionAttributeValues;
            var tagPartitionKey = values[":pk"].S;
            var rows = _items
                .Where(entry => entry.Key.Table == request.TableName && entry.Key.Pk == tagPartitionKey)
                .Select(entry => entry.Value)
                .Where(item => MatchesKeyCondition(item["sk"].S, request.KeyConditionExpression, values))
                .OrderBy(item => item["sk"].S, StringComparer.Ordinal)
                .ToList();
            var offset = request.ExclusiveStartKey is { Count: > 0 } &&
                         request.ExclusiveStartKey.TryGetValue("cursor", out var cursor)
                ? int.Parse(cursor.N, System.Globalization.CultureInfo.InvariantCulture)
                : 0;
            var page = rows.Skip(offset).Take(request.Limit ?? rows.Count).Select(Clone).ToList();
            MaximumQueryRows = Math.Max(MaximumQueryRows, page.Count);
            var nextOffset = offset + page.Count;
            return new QueryResponse
            {
                Items = page,
                Count = page.Count,
                LastEvaluatedKey = nextOffset < rows.Count
                    ? new Dictionary<string, AttributeValue>
                    {
                        ["cursor"] = new AttributeValue
                        {
                            N = nextOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        }
                    }
                    : new Dictionary<string, AttributeValue>(),
                ConsumedCapacity = new ConsumedCapacity { CapacityUnits = 1d }
            };
        }

        private static bool MatchesKeyCondition(
            string sortKey,
            string keyCondition,
            IReadOnlyDictionary<string, AttributeValue> values)
        {
            if (keyCondition.Contains("BETWEEN :since AND :until", StringComparison.Ordinal))
            {
                return string.CompareOrdinal(sortKey, values[":since"].S) >= 0 &&
                       string.CompareOrdinal(sortKey, values[":until"].S) <= 0;
            }

            if (keyCondition.Contains("sk > :since", StringComparison.Ordinal))
            {
                return string.CompareOrdinal(sortKey, values[":since"].S) > 0;
            }

            if (keyCondition.Contains("sk <= :until", StringComparison.Ordinal))
            {
                return string.CompareOrdinal(sortKey, values[":until"].S) <= 0;
            }

            return true;
        }

        private static Dictionary<string, AttributeValue> Clone(Dictionary<string, AttributeValue> item) =>
            item.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
}
