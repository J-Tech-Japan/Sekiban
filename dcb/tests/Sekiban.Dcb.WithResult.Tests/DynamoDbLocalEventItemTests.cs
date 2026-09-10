using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Dcb.Domain;
using Dcb.Domain.Student;
using Dcb.Domain.Weather;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;

namespace Sekiban.Dcb.Tests;

/// <summary>
/// AC6: real AmazonDynamoDBClient against the pinned DynamoDB Local service started by run_test_dcb.yml.
/// The test intentionally does not use AWS CLI, a request fake, or a recording client for the boundary proof.
/// </summary>
[Trait("Category", "DynamoDbLocal")]
public sealed class DynamoDbLocalEventItemTests
{
    [Fact]
    public async Task PinnedLocalAcceptsExact400KiBAndExecutorRejectsAboveItWithoutAnotherEvent()
    {
        var endpoint = Environment.GetEnvironmentVariable("SEKIBAN_DYNAMODB_LOCAL_ENDPOINT")
            ?? "http://127.0.0.1:18000";
        var suffix = Guid.NewGuid().ToString("N");
        var boundaryTableName = $"G67Boundary{suffix}";
        var options = new DynamoDbEventStoreOptions
        {
            EventsTableName = $"G67Events{suffix}",
            TagsTableName = $"G67Tags{suffix}",
            ProjectionStatesTableName = $"G67Projection{suffix}",
            AutoCreateTables = true,
            ServiceUrl = new Uri(endpoint),
            MaxRetryAttempts = 2,
            MaxRetryDelay = TimeSpan.FromMilliseconds(100)
        };

        using var client = new AmazonDynamoDBClient(
            new BasicAWSCredentials("fakeMyKeyId", "fakeSecretAccessKey"),
            new AmazonDynamoDBConfig
            {
                ServiceURL = endpoint,
                AuthenticationRegion = "us-east-1"
            });
        var context = new DynamoDbContext(client, Options.Create(options));
        var domain = DomainType.GetDomainTypes();
        var store = new DynamoDbEventStore(context, domain.EventTypes, new FixedServiceIdProvider("g67-local"));

        try
        {
            var baseEvent = CreateSerializedEvent(Array.Empty<byte>());
            var measurement = new DynamoDbEventItemSizeMeasurement(options);
            await CreateBoundaryTableAsync(client, boundaryTableName);
            var baseBytes = Measure(measurement, baseEvent);
            var exact = baseEvent with { Payload = Enumerable.Repeat(
                (byte)'x',
                checked((int)(DynamoDbEventItemSizeMeasurement.MaximumItemBytes - baseBytes))).ToArray() };

            Assert.Equal(
                DynamoDbEventItemSizeMeasurement.MaximumItemBytes,
                Measure(measurement, exact));
            var baseWrite = await store.WriteSerializableEventsAsync([baseEvent]);
            Assert.True(baseWrite.IsSuccess);

            var item = await client.GetItemAsync(new GetItemRequest
            {
                TableName = options.EventsTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["pk"] = new() { S = $"SERVICE#g67-local#EVENT#{baseEvent.Id}" },
                    ["sk"] = new() { S = $"EVENT#{baseEvent.Id}" }
                },
                ConsistentRead = true
            });
            Assert.NotEmpty(item.Item);
            item.Item["payload"] = new AttributeValue { S = Encoding.UTF8.GetString(exact.Payload) };
            Assert.Equal(
                DynamoDbEventItemSizeMeasurement.MaximumItemBytes,
                CountedItemBytes(item.Item));
            await client.PutItemAsync(new PutItemRequest
            {
                TableName = boundaryTableName,
                Item = item.Item
            });

            var tooLargeItem = item.Item.ToDictionary(pair => pair.Key, pair => pair.Value);
            tooLargeItem["payload"] = new AttributeValue
            {
                S = Encoding.UTF8.GetString(Enumerable.Repeat((byte)'x', exact.Payload.Length + 1).ToArray())
            };
            var emulatorRejection = await Assert.ThrowsAsync<AmazonDynamoDBException>(() => client.PutItemAsync(
                new PutItemRequest
                {
                    TableName = boundaryTableName,
                    Item = tooLargeItem
                }));
            Assert.Contains("exceeded", emulatorRejection.Message, StringComparison.OrdinalIgnoreCase);

            var before = await CountItemsAsync(client, options.EventsTableName);
            var oversizedPayload = JsonSerializer.SerializeToUtf8Bytes(
                new WeatherForecastCreated(
                    Guid.CreateVersion7(),
                    "local",
                    new DateOnly(2026, 9, 9),
                    1,
                    new string('x', (int)DynamoDbEventItemSizeMeasurement.MaximumItemBytes)),
                domain.JsonSerializerOptions);
            var oversizedCandidate = new SerializableEventCandidate(
                oversizedPayload,
                nameof(WeatherForecastCreated),
                []);
            var gateExecutor = new GeneralSekibanExecutor(
                store,
                new InMemoryObjectAccessor(new CoreInMemoryEventStore(domain.EventTypes), domain),
                domain,
                new ExecutorSizeGateOptions().AddDynamoDbEventItemPolicy(options));

            var rejected = await gateExecutor.CommitSerializableEventsAsync(
                new SerializedCommitRequest([oversizedCandidate], []));

            var rejection = Assert.IsType<ExecutorSizeLimitExceededException>(rejected.GetException());
            Assert.Equal(DynamoDbEventItemSizeMeasurement.Scope, rejection.Scope);
            Assert.Equal(before, await CountItemsAsync(client, options.EventsTableName));
        }
        finally
        {
            await DeleteTableAsync(client, boundaryTableName);
            await DeleteTableAsync(client, options.EventsTableName);
            await DeleteTableAsync(client, options.TagsTableName);
            await DeleteTableAsync(client, options.ProjectionStatesTableName);
        }
    }

    private static SerializableEvent CreateSerializedEvent(byte[] payload)
    {
        var id = Guid.CreateVersion7();
        return new SerializableEvent(
            payload,
            SortableUniqueId.GenerateNew(),
            id,
            new EventMetadata("cause", "correlation", "user"),
            [],
            nameof(StudentCreated));
    }

    private static long Measure(DynamoDbEventItemSizeMeasurement measurement, SerializableEvent serialized)
    {
        var result = measurement.Measure(new ExecutorSizeMeasurementContext(
            DynamoDbEventItemSizeMeasurement.Scope,
            ExecutorSizeRepresentation.StorageItem,
            new Event(
                new StudentCreated(Guid.CreateVersion7(), "measurement", 1),
                serialized.SortableUniqueIdValue,
                serialized.EventPayloadName,
                serialized.Id,
                serialized.EventMetadata,
                serialized.Tags),
            serialized,
            "g67-local",
            null,
            null));
        Assert.True(result.IsAvailable, result.Reason);
        return Assert.IsType<long>(result.Bytes);
    }

    private static long CountedItemBytes(IReadOnlyDictionary<string, AttributeValue> item) =>
        item.Sum(pair => Encoding.UTF8.GetByteCount(pair.Key) + CountedValueBytes(pair.Value));

    private static long CountedValueBytes(AttributeValue value)
    {
        if (value.S is not null)
            return Encoding.UTF8.GetByteCount(value.S);
        if (value.N is not null)
            return Encoding.UTF8.GetByteCount(value.N);
        if (value.L is { Count: > 0 })
            return 3 + value.L.Sum(x => 1 + CountedValueBytes(x));
        if (value.L is not null)
            return 3;
        if (value.M is { Count: > 0 })
            return 3 + value.M.Sum(x => 1 + Encoding.UTF8.GetByteCount(x.Key) + CountedValueBytes(x.Value));
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
        var result = await client.ScanAsync(new ScanRequest { TableName = tableName });
        return result.Count ?? result.Items.Count;
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

    private static async Task CreateBoundaryTableAsync(IAmazonDynamoDB client, string tableName)
    {
        await client.CreateTableAsync(new CreateTableRequest
        {
            TableName = tableName,
            KeySchema =
            [
                new KeySchemaElement("pk", KeyType.HASH),
                new KeySchemaElement("sk", KeyType.RANGE)
            ],
            AttributeDefinitions =
            [
                new AttributeDefinition("pk", ScalarAttributeType.S),
                new AttributeDefinition("sk", ScalarAttributeType.S)
            ],
            BillingMode = BillingMode.PAY_PER_REQUEST
        });

        for (var attempt = 0; attempt < 30; attempt++)
        {
            var description = await client.DescribeTableAsync(tableName);
            if (description.Table.TableStatus == TableStatus.ACTIVE)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new InvalidOperationException($"DynamoDB Local table {tableName} did not become active.");
    }

    private sealed class FixedServiceIdProvider(string serviceId) : IServiceIdProvider
    {
        public string GetCurrentServiceId() => serviceId;
    }
}
