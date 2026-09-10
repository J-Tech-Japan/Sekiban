using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Dcb.Domain;
using Dcb.Domain.Student;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
/// Real AmazonDynamoDBClient evidence against the pinned DynamoDB Local service used by CI.
/// The tests do not use a request fake for the persisted-map or aggregate transaction proof.
/// </summary>
[Trait("Category", "DynamoDbLocal")]
public sealed class DynamoDbLocalWriteOperationTests
{
    [Theory]
    [InlineData(4_194_303, true)]
    [InlineData(4_194_305, false)]
    public async Task PinnedLocalCharacterizesUnderAndOverFourMiBOperationContribution(
        long expectedContribution,
        bool expectedSuccess)
    {
        var endpoint = Environment.GetEnvironmentVariable("SEKIBAN_DYNAMODB_LOCAL_ENDPOINT")
            ?? "http://127.0.0.1:18000";
        var suffix = Guid.NewGuid().ToString("N");
        var serviceId = "g68-local";
        var options = CreateOptions(suffix, endpoint);
        using var client = CreateClient(endpoint);
        var domain = DomainType.GetDomainTypes();
        var store = NewStore(client, options, domain, serviceId);

        try
        {
            var measurement = new DynamoDbWriteOperationSizeMeasurement(options);
            var events = CreateOperationBoundary(measurement, expectedContribution, serviceId);
            Assert.Equal(expectedContribution, events.Sum(serialized => Measure(measurement, serialized, serviceId)));
            Assert.All(events, serialized => Assert.InRange(
                Measure(measurement, serialized, serviceId),
                1,
                DynamoDbMaxWrittenItemSizeMeasurement.MaximumItemBytes +
                Encoding.UTF8.GetByteCount("attribute_not_exists(pk)")));

            var result = await store.WriteSerializableEventsAsync(events);

            Assert.Equal(expectedSuccess, result.IsSuccess);
            Assert.Equal(expectedSuccess ? events.Count : 0, await CountItemsAsync(client, options.EventsTableName));
            if (!expectedSuccess)
                Assert.NotNull(result.GetException());
        }
        finally
        {
            await DeleteTableAsync(client, options.EventsTableName);
            await DeleteTableAsync(client, options.TagsTableName);
            await DeleteTableAsync(client, options.ProjectionStatesTableName);
        }
    }

    [Fact]
    public async Task PinnedLocalPersistedMapsMatchBothG68Measurements()
    {
        var endpoint = Environment.GetEnvironmentVariable("SEKIBAN_DYNAMODB_LOCAL_ENDPOINT")
            ?? "http://127.0.0.1:18000";
        var suffix = Guid.NewGuid().ToString("N");
        var serviceId = "g68-map";
        var options = CreateOptions(suffix, endpoint);
        using var client = CreateClient(endpoint);
        var domain = DomainType.GetDomainTypes();
        var store = NewStore(client, options, domain, serviceId);
        var eventId = Guid.CreateVersion7();
        var @event = new Event(
            new StudentCreated(eventId, "東京😀", 21),
            SortableUniqueId.GenerateNew(),
            nameof(StudentCreated),
            eventId,
            new EventMetadata("cause", "correlation", "user"),
            [$"Student:{eventId}"]);
        var serialized = @event.ToSerializableEvent(domain.EventTypes);

        try
        {
            Assert.True((await store.WriteSerializableEventsAsync([serialized])).IsSuccess);
            var eventItem = (await client.GetItemAsync(new GetItemRequest
            {
                TableName = options.EventsTableName,
                Key = EventKey(serviceId, serialized.Id),
                ConsistentRead = true
            })).Item;
            var tagItem = (await client.GetItemAsync(new GetItemRequest
            {
                TableName = options.TagsTableName,
                Key = TagKey(serviceId, serialized.Tags[0], serialized.SortableUniqueIdValue, serialized.Id),
                ConsistentRead = true
            })).Item;

            Assert.NotEmpty(eventItem);
            Assert.NotEmpty(tagItem);
            var context = new ExecutorSizeMeasurementContext(
                DynamoDbWriteOperationSizeMeasurement.Scope,
                ExecutorSizeRepresentation.StorageItem,
                @event,
                serialized,
                serviceId,
                null,
                null);
            var max = Assert.IsType<long>(new DynamoDbMaxWrittenItemSizeMeasurement(options).Measure(context).Bytes);
            var operation = Assert.IsType<long>(new DynamoDbWriteOperationSizeMeasurement(options).Measure(context).Bytes);
            var eventBytes = CountedItemBytes(eventItem);
            var tagBytes = CountedItemBytes(tagItem);

            Assert.Equal(Math.Max(eventBytes, tagBytes), max);
            Assert.Equal(
                eventBytes + tagBytes + Encoding.UTF8.GetByteCount("attribute_not_exists(pk)"),
                operation);
        }
        finally
        {
            await DeleteTableAsync(client, options.EventsTableName);
            await DeleteTableAsync(client, options.TagsTableName);
            await DeleteTableAsync(client, options.ProjectionStatesTableName);
        }
    }

    private static DynamoDbEventStoreOptions CreateOptions(string suffix, string endpoint) =>
        new()
        {
            EventsTableName = $"G68Events{suffix}",
            TagsTableName = $"G68Tags{suffix}",
            ProjectionStatesTableName = $"G68Projection{suffix}",
            AutoCreateTables = true,
            ServiceUrl = new Uri(endpoint),
            MaxRetryAttempts = 2,
            MaxRetryDelay = TimeSpan.FromMilliseconds(100)
        };

    private static AmazonDynamoDBClient CreateClient(string endpoint) =>
        new(
            new BasicAWSCredentials("fakeMyKeyId", "fakeSecretAccessKey"),
            new AmazonDynamoDBConfig
            {
                ServiceURL = endpoint,
                AuthenticationRegion = "us-east-1"
            });

    private static DynamoDbEventStore NewStore(
        IAmazonDynamoDB client,
        DynamoDbEventStoreOptions options,
        DcbDomainTypes domain,
        string serviceId) =>
        new(
            new DynamoDbContext(client, Options.Create(options)),
            domain.EventTypes,
            new FixedServiceIdProvider(serviceId));

    private static List<SerializableEvent> CreateOperationBoundary(
        DynamoDbWriteOperationSizeMeasurement measurement,
        long expectedBytes,
        string serviceId)
    {
        const int fixedEventCount = 10;
        const int fixedPayloadLength = 380_000;
        var events = Enumerable.Range(0, fixedEventCount)
            .Select(_ => CreateSerializedEvent(
                Enumerable.Repeat((byte)'x', fixedPayloadLength).ToArray(),
                "OperationEvent"))
            .ToList();
        var used = events.Sum(serialized => Measure(measurement, serialized, serviceId));
        var emptyLast = CreateSerializedEvent([], "OperationEvent");
        var lastPayloadLength = checked((int)(expectedBytes - used - Measure(measurement, emptyLast, serviceId)));
        Assert.InRange(lastPayloadLength, 0, (int)DynamoDbMaxWrittenItemSizeMeasurement.MaximumItemBytes);
        events.Add(emptyLast with
        {
            Payload = Enumerable.Repeat((byte)'x', lastPayloadLength).ToArray()
        });
        return events;
    }

    private static SerializableEvent CreateSerializedEvent(byte[] payload, string eventType) =>
        new(
            payload,
            SortableUniqueId.GenerateNew(),
            Guid.CreateVersion7(),
            new EventMetadata("cause", "correlation", "user"),
            [],
            eventType);

    private static long Measure(
        DynamoDbWriteOperationSizeMeasurement measurement,
        SerializableEvent serialized,
        string serviceId)
    {
        var result = measurement.Measure(new ExecutorSizeMeasurementContext(
            DynamoDbWriteOperationSizeMeasurement.Scope,
            ExecutorSizeRepresentation.StorageItem,
            new Event(
                new StudentCreated(Guid.CreateVersion7(), "measurement", 1),
                serialized.SortableUniqueIdValue,
                serialized.EventPayloadName,
                serialized.Id,
                serialized.EventMetadata,
                serialized.Tags),
            serialized,
            serviceId,
            null,
            null));
        Assert.True(result.IsAvailable, result.Reason);
        return Assert.IsType<long>(result.Bytes);
    }

    private static Dictionary<string, AttributeValue> EventKey(string serviceId, Guid eventId) =>
        new()
        {
            ["pk"] = new AttributeValue { S = $"SERVICE#{serviceId}#EVENT#{eventId}" },
            ["sk"] = new AttributeValue { S = $"EVENT#{eventId}" }
        };

    private static Dictionary<string, AttributeValue> TagKey(
        string serviceId,
        string tag,
        string sortableUniqueId,
        Guid eventId) =>
        new()
        {
            ["pk"] = new AttributeValue { S = $"SERVICE#{serviceId}#TAG#{tag}" },
            ["sk"] = new AttributeValue { S = $"{sortableUniqueId}#{eventId}" }
        };

    private static long CountedItemBytes(IReadOnlyDictionary<string, AttributeValue> item) =>
        item.Sum(pair => Encoding.UTF8.GetByteCount(pair.Key) + CountedValueBytes(pair.Value));

    private static long CountedValueBytes(AttributeValue value)
    {
        if (value.S is not null)
            return Encoding.UTF8.GetByteCount(value.S);
        if (value.N is not null)
            return Encoding.UTF8.GetByteCount(value.N);
        if (value.L is { Count: > 0 })
            return 3 + value.L.Sum(item => 1 + CountedValueBytes(item));
        if (value.L is not null)
            return 3;
        if (value.M is { Count: > 0 })
            return 3 + value.M.Sum(pair => 1 + Encoding.UTF8.GetByteCount(pair.Key) + CountedValueBytes(pair.Value));
        if (value.M is not null)
            return 3;
        if (value.B is not null)
            return value.B.Length;
        if (value.SS is not null)
            return value.SS.Sum(Encoding.UTF8.GetByteCount);
        if (value.NS is not null)
            return value.NS.Sum(Encoding.UTF8.GetByteCount);
        if (value.BS is not null)
            return value.BS.Sum(stream => stream.Length);
        return value.NULL == true || value.IsBOOLSet ? 1 : 0;
    }

    private static async Task<int> CountItemsAsync(IAmazonDynamoDB client, string tableName)
    {
        var total = 0;
        Dictionary<string, AttributeValue>? lastKey = null;
        do
        {
            var result = await client.ScanAsync(new ScanRequest
            {
                TableName = tableName,
                ExclusiveStartKey = lastKey
            });
            total += result.Count ?? result.Items.Count;
            lastKey = result.LastEvaluatedKey;
        }
        while (lastKey is { Count: > 0 });

        return total;
    }

    private static async Task DeleteTableAsync(IAmazonDynamoDB client, string tableName)
    {
        try
        {
            await client.DeleteTableAsync(tableName);
        }
        catch (ResourceNotFoundException)
        {
            // The test may have failed before the store created the table.
        }
    }

    private sealed class FixedServiceIdProvider(string serviceId) : IServiceIdProvider
    {
        public string GetCurrentServiceId() => serviceId;
    }
}
