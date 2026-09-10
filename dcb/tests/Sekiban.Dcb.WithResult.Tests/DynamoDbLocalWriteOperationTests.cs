using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Dcb.Domain;
using Dcb.Domain.Student;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Testing;
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
    [InlineData(4_194_304, true)]
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
            {
                var exception = result.GetException();
                Assert.Equal(expectedContribution, ReadReportedPayloadSize(exception));
            }
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

    [Fact]
    public async Task PinnedLocalItemOnlyUndercountMutantAdmitsButServiceRejects()
    {
        var endpoint = Environment.GetEnvironmentVariable("SEKIBAN_DYNAMODB_LOCAL_ENDPOINT")
            ?? "http://127.0.0.1:18000";
        var suffix = Guid.NewGuid().ToString("N");
        const string serviceId = "default";
        var options = CreateOptions(suffix, endpoint);
        using var client = CreateClient(endpoint);
        var domain = DomainType.GetDomainTypes();
        var store = NewStore(client, options, domain, serviceId);

        try
        {
            var productionMeasurement = new DynamoDbWriteOperationSizeMeasurement(options);
            var events = CreateValidExecutorOperationBoundary(productionMeasurement, 4_194_305, serviceId);
            var productionContribution = events.Sum(serialized => Measure(productionMeasurement, serialized, serviceId));
            var itemOnlyMeasurement = new ItemOnlyOperationMeasurement(options);
            var itemOnlyContribution = events.Sum(serialized => Measure(itemOnlyMeasurement, serialized, serviceId));
            var conditionBytes = Encoding.UTF8.GetByteCount("attribute_not_exists(pk)");

            Assert.Equal(4_194_305, productionContribution);
            Assert.Equal(productionContribution - (conditionBytes * events.Count), itemOnlyContribution);
            Assert.InRange(itemOnlyContribution, 1, DynamoDbWriteOperationSizeMeasurement.MaximumTransactionBytes);

            var request = new SerializedCommitRequest(
                events.Select(e => new SerializableEventCandidate(e.Payload, e.EventPayloadName, e.Tags)).ToArray(),
                []);
            var itemOnlyOptions = new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "dynamodb-item-only-mutant",
                ExecutorSizeRepresentation.StorageItem,
                maxBytesPerOperation: DynamoDbWriteOperationSizeMeasurement.MaximumTransactionBytes,
                measurement: itemOnlyMeasurement));
            var itemOnlyExecutor = new GeneralSekibanExecutor(
                store,
                new InMemoryObjectAccessor(store, domain),
                domain,
                itemOnlyOptions);

            // The item-only mutant admits the operation; the actual provider then rejects the same transaction.
            var serviceResult = await itemOnlyExecutor.CommitSerializableEventsAsync(request);
            Assert.False(serviceResult.IsSuccess);
            Assert.IsNotType<ExecutorSizeLimitExceededException>(serviceResult.GetException());
            Assert.Equal(productionContribution, ReadReportedPayloadSize(serviceResult.GetException()));
            Assert.Equal(0, await CountItemsAsync(client, options.EventsTableName));
            Assert.Equal(0, await CountItemsAsync(client, options.TagsTableName));

            var productionExecutor = new GeneralSekibanExecutor(
                store,
                new InMemoryObjectAccessor(store, domain),
                domain,
                new ExecutorSizeGateOptions().AddDynamoDbWriteOperationPolicy(options));
            var productionResult = await productionExecutor.CommitSerializableEventsAsync(request);
            var gateException = Assert.IsType<ExecutorSizeLimitExceededException>(productionResult.GetException());
            Assert.Equal(DynamoDbWriteOperationSizeMeasurement.Scope, gateException.Scope);
            Assert.Equal(productionContribution, gateException.MeasuredBytes);
            Assert.Equal(0, await CountItemsAsync(client, options.EventsTableName));
            Assert.Equal(0, await CountItemsAsync(client, options.TagsTableName));
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

    private static List<SerializableEvent> CreateValidExecutorOperationBoundary(
        DynamoDbWriteOperationSizeMeasurement measurement,
        long expectedBytes,
        string serviceId)
    {
        const int fixedEventCount = 10;
        const int fixedNameLength = 380_000;
        var events = Enumerable.Range(0, fixedEventCount)
            .Select(_ => CreateStudentEvent(fixedNameLength))
            .ToList();
        var used = events.Sum(serialized => Measure(measurement, serialized, serviceId));
        var low = 0;
        var high = 500_000;
        while (low <= high)
        {
            var nameLength = low + ((high - low) / 2);
            var candidate = CreateStudentEvent(nameLength);
            var total = used + Measure(measurement, candidate, serviceId);
            if (total == expectedBytes)
            {
                events.Add(candidate);
                return events;
            }

            if (total < expectedBytes)
                low = nameLength + 1;
            else
                high = nameLength - 1;
        }

        throw new Xunit.Sdk.XunitException(
            $"Could not construct a valid executor operation boundary of {expectedBytes} bytes.");
    }

    private static SerializableEvent CreateStudentEvent(int nameLength)
    {
        var id = Guid.CreateVersion7();
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new StudentCreated(id, new string('x', nameLength), 1),
            DomainType.GetDomainTypes().JsonSerializerOptions);
        return new SerializableEvent(
            payload,
            SortableUniqueId.GenerateNew(),
            id,
            new EventMetadata(id.ToString(), "SerializedCommit", "SerializedSekibanExecutor"),
            [],
            nameof(StudentCreated));
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
        IExecutorSizeMeasurement measurement,
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

    private static long ReadReportedPayloadSize(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var match = Regex.Match(
                current.Message,
                @"Payload\s+Size:\s*([0-9,]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success && long.TryParse(
                    match.Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var payloadSize))
            {
                return payloadSize;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"DynamoDB Local did not report a transaction payload size. Exception tree: {exception}");
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

    private sealed class ItemOnlyOperationMeasurement(DynamoDbEventStoreOptions options) : IExecutorSizeMeasurement
    {
        private readonly DynamoDbWriteOperationSizeMeasurement _productionMeasurement =
            new(options);

        public ExecutorSizeMeasurementResult Measure(ExecutorSizeMeasurementContext context)
        {
            var result = _productionMeasurement.Measure(context);
            if (!result.IsAvailable || result.Bytes is not { } bytes)
                return result;

            return ExecutorSizeMeasurementResult.Exact(
                bytes - Encoding.UTF8.GetByteCount("attribute_not_exists(pk)"));
        }
    }
}
