using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Dcb.Domain;
using Dcb.Domain.Student;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
/// G69 proofs for the batch-write option snapshot. The guarded client rejects the service-invalid empty and oversized
/// request shapes, while the operation tests prove that the production path never creates them for a valid snapshot.
/// </summary>
public sealed class DynamoDbBatchWriteSnapshotTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(26)]
    public async Task InvalidBatchWriteSizes_AreReturnedForTypedAndSerializedBeforeAnyWrite(int invalidSize)
    {
        var (store, options, client) = NewStore(maxTransactionItems: 1);
        options.MaxBatchWriteItems = invalidSize;

        var typed = await store.WriteEventsAsync([CreateTypedEvent("typed-invalid")]);
        AssertInvalidBatchWriteOption(typed.GetException(), invalidSize);
        Assert.Empty(client.BatchRequests);
        Assert.Empty(client.TransactionRequests);

        client.ClearRequests();
        var serialized = await store.WriteSerializableEventsAsync([CreateSerializedEvent(["serialized-invalid"])]);
        AssertInvalidBatchWriteOption(serialized.GetException(), invalidSize);
        Assert.Empty(client.BatchRequests);
        Assert.Empty(client.TransactionRequests);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    public async Task ValidBatchWriteSizes_AreUsedForEventAndTagRequests(int chunkSize)
    {
        var (store, options, client) = NewStore(maxTransactionItems: 1);
        options.MaxBatchWriteItems = chunkSize;

        var result = await store.WriteSerializableEventsAsync([CreateSerializedEvent(
            Enumerable.Range(0, 100).Select(index => $"tag-{index}").ToArray())]);

        Assert.True(result.IsSuccess);
        Assert.Equal([1], RequestSizes(client, options.EventsTableName));
        Assert.Equal(
            Enumerable.Repeat(chunkSize, 100 / chunkSize)
                .Append(100 % chunkSize)
                .Where(size => size > 0),
            RequestSizes(client, options.TagsTableName));
    }

    [Fact]
    public async Task MidOperationOptionMutation_UsesOneSnapshotForEventsAndTags()
    {
        var (store, options, client) = NewStore(maxTransactionItems: 100);
        options.MaxBatchWriteItems = 25;
        client.AfterBatchWrite = (tableName, dispatchNumber) =>
        {
            if (dispatchNumber == 1 && tableName == options.EventsTableName)
                options.MaxBatchWriteItems = 0;
        };

        var result = await store.WriteSerializableEventsAsync(CreateMultiEventBatch());

        Assert.True(result.IsSuccess);
        Assert.Equal([25, 1], RequestSizes(client, options.EventsTableName));
        Assert.Equal([25, 25, 25, 25, 25], RequestSizes(client, options.TagsTableName));
        Assert.Equal(0, options.MaxBatchWriteItems);
    }

    [Fact]
    public async Task TagFailure_RollsBackWithTheSameSnapshotAndPreservesOriginalException()
    {
        var (store, options, client) = NewStore(maxTransactionItems: 100);
        options.MaxBatchWriteItems = 25;
        var failure = new InvalidOperationException("injected tag failure");
        client.AfterBatchWrite = (tableName, dispatchNumber) =>
        {
            if (dispatchNumber == 1 && tableName == options.EventsTableName)
                options.MaxBatchWriteItems = 0;
        };
        client.BatchWriteFailure = (tableName, _) =>
            tableName == options.TagsTableName ? failure : null;

        var result = await store.WriteSerializableEventsAsync(CreateMultiEventBatch());

        Assert.False(result.IsSuccess);
        Assert.Same(failure, result.GetException());
        Assert.Equal([25, 1, 25, 1], RequestSizes(client, options.EventsTableName));
        Assert.Equal([25], RequestSizes(client, options.TagsTableName));
        Assert.Empty(client.PersistedEventKeys);
    }

    [Fact]
    public async Task NextOperationReadsAChangedBatchSizeAfterThePreviousOperation()
    {
        var (store, options, client) = NewStore(maxTransactionItems: 1);
        options.MaxBatchWriteItems = 25;

        var first = await store.WriteSerializableEventsAsync([CreateSerializedEvent(["first"]) ]);
        Assert.True(first.IsSuccess);

        client.ClearRequests();
        options.MaxBatchWriteItems = 10;
        var second = await store.WriteSerializableEventsAsync(CreateSimpleEventBatch(26));

        Assert.True(second.IsSuccess);
        Assert.Equal([10, 10, 6], RequestSizes(client, options.EventsTableName));
        Assert.Equal([10, 10, 6], RequestSizes(client, options.TagsTableName));
    }

    [Fact]
    public async Task InvalidBatchOptionDoesNotAffectTransactionFallbackOrConditionalPaths()
    {
        var (store, options, client) = NewStore(maxTransactionItems: 100);
        options.MaxBatchWriteItems = 0;

        var result = await store.WriteSerializableEventsAsync([CreateSerializedEvent(["transaction"]) ]);

        Assert.True(result.IsSuccess);
        Assert.Empty(client.BatchRequests);
        Assert.Single(client.TransactionRequests);
    }

    [Fact]
    public async Task GuardedClientRejectsServiceInvalidEmptyAndOversizedBatchRequests()
    {
        var client = CreateClient();

        foreach (var count in new[] { 0, 26 })
        {
            var request = new BatchWriteItemRequest
            {
                RequestItems = new Dictionary<string, List<WriteRequest>>
                {
                    ["events"] = Enumerable.Range(0, count)
                        .Select(_ => new WriteRequest { PutRequest = new PutRequest { Item = [] } })
                        .ToList()
                }
            };

            await Assert.ThrowsAsync<AmazonDynamoDBException>(() => client.Client.BatchWriteItemAsync(request));
        }
    }

    private static void AssertInvalidBatchWriteOption(Exception exception, int expectedValue)
    {
        var argument = Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal(nameof(DynamoDbEventStoreOptions.MaxBatchWriteItems), argument.ParamName);
        Assert.Equal(expectedValue, argument.ActualValue);
    }

    private static (DynamoDbEventStore Store, DynamoDbEventStoreOptions Options, RecordingDynamoDb Client) NewStore(
        int maxTransactionItems)
    {
        var options = new DynamoDbEventStoreOptions
        {
            AutoCreateTables = false,
            EventsTableName = $"g69-events-{Guid.NewGuid():N}",
            TagsTableName = $"g69-tags-{Guid.NewGuid():N}",
            ProjectionStatesTableName = $"g69-projection-{Guid.NewGuid():N}",
            MaxTransactionItems = maxTransactionItems
        };
        var client = CreateClient();
        var domain = DomainType.GetDomainTypes();
        var store = new DynamoDbEventStore(
            new DynamoDbContext(client.Client, Options.Create(options)),
            domain.EventTypes,
            new FixedServiceIdProvider("g69"));
        return (store, options, client);
    }

    private static RecordingDynamoDb CreateClient()
    {
        var client = System.Reflection.DispatchProxy.Create<IAmazonDynamoDB, RecordingDynamoDb>();
        return (RecordingDynamoDb)(object)client;
    }

    private static List<int> RequestSizes(RecordingDynamoDb client, string tableName) =>
        client.BatchRequests
            .Where(request => request.RequestItems.Count == 1 && request.RequestItems.ContainsKey(tableName))
            .Select(request => request.RequestItems[tableName].Count)
            .ToList();

    private static List<SerializableEvent> CreateMultiEventBatch()
    {
        return Enumerable.Range(0, 26)
            .Select(index => CreateSerializedEvent(
                index == 0
                    ? Enumerable.Range(0, 100).Select(tagIndex => $"large-tag-{tagIndex}").ToArray()
                    : [$"tag-{index}"],
                $"event-{index}"))
            .ToList();
    }

    private static List<SerializableEvent> CreateSimpleEventBatch(int count) =>
        Enumerable.Range(0, count)
            .Select(index => CreateSerializedEvent([$"next-{index}"]))
            .ToList();

    private static Event CreateTypedEvent(string tag)
    {
        var id = Guid.CreateVersion7();
        return new Event(
            new StudentCreated(id, "g69", 1),
            SortableUniqueId.GenerateNew(),
            nameof(StudentCreated),
            id,
            new EventMetadata("cause", "correlation", "user"),
            [tag]);
    }

    private static SerializableEvent CreateSerializedEvent(
        IReadOnlyList<string> tags,
        string eventType = nameof(StudentCreated)) =>
        new(
            [],
            SortableUniqueId.GenerateNew(),
            Guid.CreateVersion7(),
            new EventMetadata("cause", "correlation", "user"),
            tags.ToList(),
            eventType);

    private sealed class FixedServiceIdProvider(string serviceId) : IServiceIdProvider
    {
        public string GetCurrentServiceId() => serviceId;
    }

    public class RecordingDynamoDb : System.Reflection.DispatchProxy
    {
        public List<BatchWriteItemRequest> BatchRequests { get; } = [];
        public List<TransactWriteItemsRequest> TransactionRequests { get; } = [];
        public HashSet<string> PersistedEventKeys { get; } = [];
        public Action<string, int>? AfterBatchWrite { get; set; }
        public Func<string, int, Exception?>? BatchWriteFailure { get; set; }
        public IAmazonDynamoDB Client => (IAmazonDynamoDB)(object)this;

        public void ClearRequests()
        {
            BatchRequests.Clear();
            TransactionRequests.Clear();
        }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod?.Name ?? string.Empty;
            var argument = args is { Length: > 0 } ? args[0] : null;

            if (name == nameof(IAmazonDynamoDB.BatchWriteItemAsync) && argument is BatchWriteItemRequest batch)
            {
                BatchRequests.Add(batch);
                var dispatchNumber = BatchRequests.Count;
                foreach (var (tableName, writes) in batch.RequestItems)
                {
                    if (writes.Count is < 1 or > 25)
                        throw new AmazonDynamoDBException(
                            $"BatchWriteItem request for {tableName} contained {writes.Count} items.");

                    var failure = BatchWriteFailure?.Invoke(tableName, dispatchNumber);
                    if (failure is not null)
                        throw failure;

                    foreach (var write in writes)
                    {
                        if (write.PutRequest?.Item is { } put)
                            PersistedEventKeys.Add(ItemKey(put));
                        else if (write.DeleteRequest?.Key is { } delete)
                            PersistedEventKeys.Remove(ItemKey(delete));
                    }

                    AfterBatchWrite?.Invoke(tableName, dispatchNumber);
                }

                return Task.FromResult(new BatchWriteItemResponse
                {
                    UnprocessedItems = new Dictionary<string, List<WriteRequest>>()
                });
            }

            if (name == nameof(IAmazonDynamoDB.TransactWriteItemsAsync) &&
                argument is TransactWriteItemsRequest transaction)
            {
                TransactionRequests.Add(transaction);
                foreach (var item in transaction.TransactItems)
                {
                    if (item.Put?.Item is { } put)
                        PersistedEventKeys.Add(ItemKey(put));
                }

                return Task.FromResult(new TransactWriteItemsResponse());
            }

            if (name == "Dispose" || name.StartsWith("get_", StringComparison.Ordinal))
                return null;

            throw new NotSupportedException($"RecordingDynamoDb does not support {name}");
        }

        private static string ItemKey(IReadOnlyDictionary<string, AttributeValue> item) =>
            string.Join("|", item.TryGetValue("pk", out var pk) ? pk.S : string.Empty,
                item.TryGetValue("sk", out var sk) ? sk.S : string.Empty);
    }
}
