using System.Reflection;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Dcb.Domain;
using Dcb.Domain.Student;
using Dcb.Domain.Weather;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using Microsoft.Extensions.Options;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;

namespace Sekiban.Dcb.Tests;

/// <summary>
/// G67 provider mapping and gate proofs. The expected byte calculation in this file is intentionally independent
/// from the production calculator; the production map is captured from the real store and the oracle only applies
/// AWS's AttributeValue accounting to that captured request shape.
/// </summary>
public sealed class DynamoDbEventItemSizeMeasurementTests
{
    [Fact]
    public async Task TypedSerializedAndConditionalRoutesShareTheCanonicalEventItemShape()
    {
        var domain = DomainType.GetDomainTypes();
        var eventId = Guid.CreateVersion7();
        var @event = new Event(
            new StudentCreated(Guid.CreateVersion7(), "東京😀", 21),
            SortableUniqueId.GenerateNew(),
            nameof(StudentCreated),
            eventId,
            new EventMetadata("因果", "相関", "ユーザー"),
            ["Student:東京😀", "タグ"]);
        var serialized = @event.ToSerializableEvent(domain.EventTypes);
        var client = DispatchProxy.Create<IAmazonDynamoDB, CapturingDynamoDb>();
        var capture = (CapturingDynamoDb)(object)client;
        var store = NewStore(client, domain, "shape");

        Assert.True((await store.WriteEventsAsync([@event])).IsSuccess);
        var typedEvent = capture.EventItems.Single();
        capture.Clear();

        Assert.True((await store.WriteSerializableEventsAsync([serialized])).IsSuccess);
        var serializedEvent = capture.EventItems.Single();
        Assert.Equal(2, capture.TagItems.Count);
        capture.Clear();

        var conditional = await store.AppendIfUniqueAsync(
            new ConditionalAppendRequest("shape-conditional", serialized));
        Assert.True(conditional.IsSuccess);
        var conditionalEvent = capture.EventItems.Single();

        Assert.Equal(Normalize(typedEvent, "pk", "sk", "eventId", "timestamp"),
            Normalize(serializedEvent, "pk", "sk", "eventId", "timestamp"));
        Assert.Equal(Normalize(typedEvent, "pk", "sk", "eventId", "timestamp"),
            Normalize(conditionalEvent, "pk", "sk", "eventId", "timestamp"));
        Assert.Equal(serializedEvent["payload"].S, conditionalEvent["payload"].S);
        Assert.Equal(serializedEvent["gsi1pk"].S, conditionalEvent["gsi1pk"].S);
        Assert.Equal(serializedEvent["tags"].L.Select(x => x.S), conditionalEvent["tags"].L.Select(x => x.S));
    }

    [Fact]
    public async Task MeasurementMatchesAnIndependentUtf8AndListOracleIncludingOptionalMetadata()
    {
        var domain = DomainType.GetDomainTypes();
        var @event = new Event(
            new StudentCreated(Guid.CreateVersion7(), "名前😀", 42),
            SortableUniqueId.GenerateNew(),
            nameof(StudentCreated),
            Guid.CreateVersion7(),
            new EventMetadata("cause-東京", "correlation", "ユーザー"),
            ["Student:東京", "tag😀"]);
        var serialized = @event.ToSerializableEvent(domain.EventTypes);
        var client = DispatchProxy.Create<IAmazonDynamoDB, CapturingDynamoDb>();
        var capture = (CapturingDynamoDb)(object)client;
        var store = NewStore(client, domain, "oracle");
        Assert.True((await store.WriteSerializableEventsAsync([serialized])).IsSuccess);

        var measurement = new DynamoDbEventItemSizeMeasurement(new DynamoDbEventStoreOptions { WriteShardCount = 1 });
        var measured = measurement.Measure(new ExecutorSizeMeasurementContext(
            DynamoDbEventItemSizeMeasurement.Scope,
            ExecutorSizeRepresentation.StorageItem,
            @event,
            serialized,
            "oracle",
            null,
            null));

        Assert.True(measured.IsAvailable);
        Assert.Equal(IndependentItemBytes(capture.EventItems.Single()), measured.Bytes);
    }

    [Theory]
    [InlineData(409599)]
    [InlineData(409600)]
    [InlineData(409601)]
    public void MeasurementMatchesTheDynamoDbItemBoundary(long expectedBytes)
    {
        var empty = new SerializableEvent(
            [],
            SortableUniqueId.GenerateNew(),
            Guid.CreateVersion7(),
            new EventMetadata("cause", "correlation", "user"),
            [],
            nameof(StudentCreated));
        var measurement = new DynamoDbEventItemSizeMeasurement();
        var emptyBytes = MeasureSerialized(measurement, empty);
        var sized = empty with
        {
            Payload = Enumerable.Repeat(
                (byte)'x',
                checked((int)(expectedBytes - emptyBytes))).ToArray()
        };

        Assert.Equal(expectedBytes, MeasureSerialized(measurement, sized));
    }

    [Fact]
    public void ProviderRegistrationDefaultsToTheServiceCeilingAndRejectsAnImpossibleQuota()
    {
        var options = new ExecutorSizeGateOptions().AddDynamoDbEventItemPolicy();
        var policy = Assert.Single(options.Policies);
        Assert.Equal(DynamoDbEventItemSizeMeasurement.Scope, policy.Scope);
        Assert.Equal(ExecutorSizeRepresentation.StorageItem, policy.Representation);
        Assert.Equal(DynamoDbEventItemSizeMeasurement.MaximumItemBytes, policy.MaxBytesPerEvent);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExecutorSizeGateOptions().AddDynamoDbEventItemPolicy(
                maxBytesPerEvent: DynamoDbEventItemSizeMeasurement.MaximumItemBytes + 1));
    }

    [Fact]
    public async Task ProviderMeasurementRejectsAnOversizedLastEventBeforeTheStore()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CoreInMemoryEventStore(domain.EventTypes);
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(store, domain),
            domain,
            new ExecutorSizeGateOptions().AddDynamoDbEventItemPolicy());
        var eventId = Guid.CreateVersion7();
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new WeatherForecastCreated(
                eventId,
                "東京",
                new DateOnly(2026, 9, 9),
                21,
                new string('x', (int)DynamoDbEventItemSizeMeasurement.MaximumItemBytes))
            , domain.JsonSerializerOptions);
        var request = new SerializedCommitRequest(
            [new SerializableEventCandidate(payload, nameof(WeatherForecastCreated), [])],
            []);

        var result = await executor.CommitSerializableEventsAsync(request);

        Assert.IsType<ExecutorSizeLimitExceededException>(result.GetException());
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    private static DynamoDbEventStore NewStore(
        IAmazonDynamoDB client,
        DcbDomainTypes domain,
        string serviceId)
    {
        var options = Options.Create(new DynamoDbEventStoreOptions
        {
            AutoCreateTables = false,
            EventsTableName = $"events-{serviceId}",
            TagsTableName = $"tags-{serviceId}",
            ProjectionStatesTableName = $"projection-{serviceId}"
        });
        return new DynamoDbEventStore(
            new DynamoDbContext(client, options),
            domain.EventTypes,
            new FixedServiceIdProvider(serviceId));
    }

    private static Dictionary<string, string> Normalize(
        IReadOnlyDictionary<string, AttributeValue> item,
        params string[] ignored)
    {
        var ignoredSet = ignored.ToHashSet(StringComparer.Ordinal);
        return item
            .Where(pair => !ignoredSet.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => ValueSignature(pair.Value), StringComparer.Ordinal);
    }

    private static string ValueSignature(AttributeValue value) =>
        value.S is not null
            ? $"S:{value.S}"
            : value.L is not null
                ? $"L:[{string.Join(",", value.L.Select(ValueSignature))}]"
                : value.N is not null
                    ? $"N:{value.N}"
                    : "other";

    private static long IndependentItemBytes(IReadOnlyDictionary<string, AttributeValue> item) =>
        item.Sum(pair => Encoding.UTF8.GetByteCount(pair.Key) + IndependentValueBytes(pair.Value));

    private static long MeasureSerialized(
        DynamoDbEventItemSizeMeasurement measurement,
        SerializableEvent serialized)
    {
        var result = measurement.Measure(new ExecutorSizeMeasurementContext(
            DynamoDbEventItemSizeMeasurement.Scope,
            ExecutorSizeRepresentation.StorageItem,
            new Event(
                new StudentCreated(Guid.CreateVersion7(), "boundary", 1),
                serialized.SortableUniqueIdValue,
                serialized.EventPayloadName,
                serialized.Id,
                serialized.EventMetadata,
                serialized.Tags),
            serialized,
            "boundary",
            null,
            null));
        Assert.True(result.IsAvailable, result.Reason);
        return Assert.IsType<long>(result.Bytes);
    }

    private static long IndependentValueBytes(AttributeValue value)
    {
        if (value.S is not null)
            return Encoding.UTF8.GetByteCount(value.S);
        if (value.N is not null)
            return Encoding.UTF8.GetByteCount(value.N);
        if (value.L is not null)
            return 3 + value.L.Sum(item => 1 + IndependentValueBytes(item));
        if (value.M is not null)
            return 3 + value.M.Sum(pair =>
                1 + Encoding.UTF8.GetByteCount(pair.Key) + IndependentValueBytes(pair.Value));
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

    private sealed class FixedServiceIdProvider(string serviceId) : IServiceIdProvider
    {
        public string GetCurrentServiceId() => serviceId;
    }

    public class CapturingDynamoDb : DispatchProxy
    {
        public List<Dictionary<string, AttributeValue>> EventItems { get; } = [];
        public List<Dictionary<string, AttributeValue>> TagItems { get; } = [];

        public void Clear()
        {
            EventItems.Clear();
            TagItems.Clear();
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod?.Name ?? string.Empty;
            var arg0 = args is { Length: > 0 } ? args[0] : null;

            if (arg0 is TransactWriteItemsRequest transaction && name == nameof(IAmazonDynamoDB.TransactWriteItemsAsync))
            {
                foreach (var item in transaction.TransactItems)
                {
                    if (item.Put is not { } put)
                        continue;
                    if (put.TableName.StartsWith("events-", StringComparison.Ordinal))
                        EventItems.Add(put.Item);
                    else
                        TagItems.Add(put.Item);
                }

                return Task.FromResult(new TransactWriteItemsResponse());
            }

            if (arg0 is BatchWriteItemRequest batch && name == nameof(IAmazonDynamoDB.BatchWriteItemAsync))
            {
                foreach (var writes in batch.RequestItems.Values)
                {
                    foreach (var write in writes)
                    {
                        if (write.PutRequest is not { } put)
                            continue;
                        if (put.Item.ContainsKey("eventType"))
                            EventItems.Add(put.Item);
                        else
                            TagItems.Add(put.Item);
                    }
                }

                return Task.FromResult(new BatchWriteItemResponse { UnprocessedItems = [] });
            }

            if (name == "Dispose" || name.StartsWith("get_", StringComparison.Ordinal))
                return null;

            throw new NotSupportedException($"CapturingDynamoDb does not support {name}");
        }
    }
}
