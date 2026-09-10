using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2.Model.Internal.MarshallTransformations;
using Amazon.Runtime.Internal;
using Dcb.Domain;
using Dcb.Domain.Student;
using Dcb.Domain.Weather;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
/// G68 provider accounting proofs. The production mapper is exercised through the store and the independent oracle
/// counts the captured AttributeValue maps rather than reconstructing a second provider model.
/// </summary>
public sealed class DynamoDbWriteOperationSizeMeasurementTests
{
    [Fact]
    public void NewPoliciesExposeOnlyTheirRelevantQuotaAndCanCoexist()
    {
        var options = new ExecutorSizeGateOptions()
            .AddDynamoDbMaxWrittenItemPolicy(maxBytesPerEvent: 1234)
            .AddDynamoDbWriteOperationPolicy(maxBytesPerOperation: 5678);

        Assert.Collection(
            options.Policies,
            item =>
            {
                Assert.Equal(DynamoDbMaxWrittenItemSizeMeasurement.Scope, item.Scope);
                Assert.Equal(1234, item.MaxBytesPerEvent);
                Assert.Null(item.MaxBytesPerOperation);
            },
            operation =>
            {
                Assert.Equal(DynamoDbWriteOperationSizeMeasurement.Scope, operation.Scope);
                Assert.Null(operation.MaxBytesPerEvent);
                Assert.Equal(5678, operation.MaxBytesPerOperation);
            });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExecutorSizeGateOptions().AddDynamoDbMaxWrittenItemPolicy(
                maxBytesPerEvent: DynamoDbMaxWrittenItemSizeMeasurement.MaximumItemBytes + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExecutorSizeGateOptions().AddDynamoDbWriteOperationPolicy(
                maxBytesPerOperation: DynamoDbWriteOperationSizeMeasurement.MaximumTransactionBytes + 1));
    }

    [Fact]
    public void DiRegistrationAggregatesG67AndBothG68PoliciesWithStoreOptions()
    {
        var client = System.Reflection.DispatchProxy.Create<IAmazonDynamoDB,
            DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb>();
        using var provider = new ServiceCollection()
            .AddSekibanDcbDynamoDb(client, options => options.WriteShardCount = 2)
            .AddSekibanDcbDynamoDbEventItemSizeGate()
            .AddSekibanDcbDynamoDbMaxWrittenItemSizeGate()
            .AddSekibanDcbDynamoDbWriteOperationSizeGate()
            .BuildServiceProvider();

        var gate = provider.GetRequiredService<ExecutorSizeGateOptions>();
        Assert.Equal(
            [
                DynamoDbEventItemSizeMeasurement.Scope,
                DynamoDbMaxWrittenItemSizeMeasurement.Scope,
                DynamoDbWriteOperationSizeMeasurement.Scope
            ],
            gate.Policies.Select(policy => policy.Scope));

        var context = CreateContext(CreateSerializedEvent([], tags: []));
        var operation = Assert.IsType<DynamoDbWriteOperationSizeMeasurement>(
            gate.Policies.Single(policy => policy.Scope == DynamoDbWriteOperationSizeMeasurement.Scope).Measurement);
        Assert.True(operation.Measure(context).IsAvailable);
    }

    [Theory]
    [InlineData(409599)]
    [InlineData(409600)]
    [InlineData(409601)]
    public void LargestEventItemBoundary_IsMeasuredExactly(long expectedBytes)
    {
        var measurement = new DynamoDbMaxWrittenItemSizeMeasurement();
        var empty = CreateSerializedEvent([], tags: []);
        var emptyBytes = Measure(measurement, empty);
        var sized = empty with
        {
            Payload = Enumerable.Repeat(
                (byte)'x',
                checked((int)(expectedBytes - emptyBytes))).ToArray()
        };

        Assert.Equal(expectedBytes, Measure(measurement, sized));
    }

    [Theory]
    [InlineData(409599)]
    [InlineData(409600)]
    [InlineData(409601)]
    public void LargestTagItemBoundary_IsMeasuredExactly(long expectedBytes)
    {
        var measurement = new DynamoDbMaxWrittenItemSizeMeasurement();
        var sized = CreateTagLargestBoundary(measurement, expectedBytes, "tag-boundary");

        Assert.Equal(expectedBytes, Measure(measurement, sized, "tag-boundary"));
        var mapped = CaptureSerializedWrite(sized, "tag-boundary");
        Assert.Equal(
            expectedBytes,
            mapped.EventItems.Concat(mapped.TagItems).Max(IndependentItemBytes));
        Assert.True(mapped.TagItems.Max(IndependentItemBytes) > mapped.EventItems.Max(IndependentItemBytes));
    }

    [Theory]
    [InlineData(4194303)]
    [InlineData(4194304)]
    [InlineData(4194305)]
    public void OperationBoundaryIncludesEveryItemAndTheProductionCondition(long expectedBytes)
    {
        var measurement = new DynamoDbWriteOperationSizeMeasurement();
        var events = CreateOperationBoundary(measurement, expectedBytes);

        Assert.All(events, serialized =>
            Assert.InRange(
                Measure(measurement, serialized),
                1,
                DynamoDbMaxWrittenItemSizeMeasurement.MaximumItemBytes +
                Encoding.UTF8.GetByteCount(DynamoDbTransactionSizeAccountingForTest.ExpectedCondition)));
        Assert.Equal(expectedBytes, events.Sum(serialized => Measure(measurement, serialized)));
    }

    [Fact]
    public async Task BatchWriteSdkBodyCanExceedSixteenMiBWhileTwentyFiveStoredItemsRemainLegal()
    {
        var domain = DomainType.GetDomainTypes();
        var client = System.Reflection.DispatchProxy.Create<IAmazonDynamoDB,
            DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb>();
        var capture = (DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb)(object)client;
        var store = NewStore(client, domain, "batch-wire", maxTransactionItems: 0);
        var escapedJsonPayload = JsonSerializer.SerializeToUtf8Bytes(
            new string('é', 194_000),
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var events = Enumerable.Range(0, 25)
            .Select(_ => CreateSerializedEvent(escapedJsonPayload, [], "BatchWireEvent"))
            .ToArray();

        var result = await store.WriteSerializableEventsAsync(events);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(capture.BatchRequests);
        var requests = Assert.Single(request.RequestItems).Value;
        Assert.Equal(25, requests.Count);
        var storedItems = requests
            .Select(write =>
            {
                Assert.NotNull(write.PutRequest);
                return write.PutRequest!.Item;
            })
            .ToArray();
        Assert.All(storedItems, item => Assert.InRange(
            IndependentItemBytes(item),
            1,
            DynamoDbMaxWrittenItemSizeMeasurement.MaximumItemBytes));
        Assert.InRange(
            storedItems.Sum(IndependentItemBytes),
            1,
            16 * 1024 * 1024 - 1);

        var sdkRequest = BatchWriteItemRequestMarshaller.Instance.Marshall(request);
        Assert.True(
            sdkRequest.ContentStream is not null || sdkRequest.Content is not null,
            $"Marshaller returned no content; parameters={sdkRequest.Parameters.Count}, set={sdkRequest.SetContentFromParameters}.");
        var bodyBytes = sdkRequest.ContentStream is not null
            ? ReadStream(sdkRequest.ContentStream)
            : sdkRequest.Content!;
        Assert.True(
            bodyBytes.Length > 16 * 1024 * 1024,
            $"SDK BatchWriteItem body was {bodyBytes.Length} bytes.");
    }

    private static byte[] ReadStream(Stream stream)
    {
        if (stream.CanSeek)
            stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public async Task TypedSerializedAndConditionalMapsShareOperationAccountingAndConditionLiteral()
    {
        var domain = DomainType.GetDomainTypes();
        var @event = new Event(
            new StudentCreated(Guid.CreateVersion7(), "東京😀", 21),
            SortableUniqueId.GenerateNew(),
            nameof(StudentCreated),
            Guid.CreateVersion7(),
            new EventMetadata("cause", "correlation", "user"),
            ["Student:東京😀", "tag"]);
        var serialized = @event.ToSerializableEvent(domain.EventTypes);
        var client = System.Reflection.DispatchProxy.Create<IAmazonDynamoDB,
            DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb>();
        var capture = (DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb)(object)client;
        var store = NewStore(client, domain, "operation-shape");
        var measurement = new DynamoDbWriteOperationSizeMeasurement();

        Assert.True((await store.WriteEventsAsync([@event])).IsSuccess);
        var typedEvent = capture.EventItems.Single();
        var typedTags = capture.TagItems.ToArray();
        Assert.Contains(
            DynamoDbTransactionSizeAccountingForTest.ExpectedCondition,
            capture.ConditionExpressions);
        Assert.Equal(
            capture.TagItems.Count,
            capture.ConditionExpressions.Count(condition => condition is null));
        Assert.NotEmpty(capture.ClientRequestTokens);
        capture.Clear();

        Assert.True((await store.WriteSerializableEventsAsync([serialized])).IsSuccess);
        var serializedEvent = capture.EventItems.Single();
        var serializedTags = capture.TagItems.ToArray();
        capture.Clear();

        Assert.True((await store.AppendIfUniqueAsync(
            new ConditionalAppendRequest("operation-shape", serialized))).IsSuccess);
        var conditionalEvent = capture.EventItems.Single();
        var conditionalTags = capture.TagItems.ToArray();
        Assert.Equal([null], capture.ClientRequestTokens);
        Assert.Contains(
            DynamoDbTransactionSizeAccountingForTest.ExpectedCondition,
            capture.ConditionExpressions);
        Assert.Equal(
            capture.TagItems.Count,
            capture.ConditionExpressions.Count(condition => condition is null));

        Assert.Equal(Normalize(typedEvent), Normalize(serializedEvent));
        Assert.Equal(Normalize(typedEvent), Normalize(conditionalEvent));
        Assert.Equal(
            typedTags.Select(Normalize),
            serializedTags.Select(Normalize));
        Assert.Equal(
            typedTags.Select(Normalize),
            conditionalTags.Select(Normalize));

        var measured = Measure(measurement, serialized, "operation-shape");
        var expected = IndependentItemBytes(serializedEvent)
            + typedTags.Sum(IndependentItemBytes)
            + Encoding.UTF8.GetByteCount(DynamoDbTransactionSizeAccountingForTest.ExpectedCondition);
        Assert.Equal(expected, measured);
    }

    [Fact]
    public async Task ConditionalRouteWithManyLegalTagRowsCountsLargeEventTypeInTheAggregate()
    {
        const int tagPayloadLength = 1_000;
        var eventType = "ConditionalAggregateEvent" + new string('e', 40_000);
        var tags = Enumerable.Range(0, 99)
            .Select(index => $"ConditionalTag{index}-" + new string('x', tagPayloadLength))
            .ToArray();
        var domain = DomainType.GetDomainTypes();
        var eventTypes = new SimpleEventTypes(domain.JsonSerializerOptions);
        eventTypes.RegisterEventType<StudentCreated>(eventType);
        var serialized = CreateSerializedEvent(
            JsonSerializer.SerializeToUtf8Bytes(
                new StudentCreated(Guid.CreateVersion7(), "conditional", 1),
                domain.JsonSerializerOptions),
            tags,
            eventType);
        var client = System.Reflection.DispatchProxy.Create<IAmazonDynamoDB,
            DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb>();
        var capture = (DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb)(object)client;
        var store = NewStore(client, domain, "conditional-aggregate", eventTypes: eventTypes);

        var append = await store.AppendIfUniqueAsync(
            new ConditionalAppendRequest("conditional-aggregate-key", serialized));
        if (!append.IsSuccess)
            Assert.Fail(append.GetException().ToString());

        var eventItem = Assert.Single(capture.EventItems);
        Assert.Equal(tags.Length, capture.TagItems.Count);
        Assert.Equal(
            DynamoDbTransactionSizeAccountingForTest.ExpectedCondition,
            Assert.Single(capture.ConditionExpressions, condition => condition is not null));
        Assert.All(capture.EventItems.Concat(capture.TagItems), item => Assert.InRange(
            IndependentItemBytes(item),
            1,
            DynamoDbMaxWrittenItemSizeMeasurement.MaximumItemBytes));

        var measured = new DynamoDbWriteOperationSizeMeasurement().Measure(
            new ExecutorSizeMeasurementContext(
                DynamoDbWriteOperationSizeMeasurement.Scope,
                ExecutorSizeRepresentation.StorageItem,
                new Event(
                    new StudentCreated(Guid.CreateVersion7(), "conditional", 1),
                    serialized.SortableUniqueIdValue,
                    eventType,
                    serialized.Id,
                    serialized.EventMetadata,
                    tags.ToList()),
                serialized,
                "conditional-aggregate",
                null,
                null));

        Assert.True(measured.IsAvailable, measured.Reason);
        var expected = IndependentItemBytes(eventItem)
            + capture.TagItems.Sum(IndependentItemBytes)
            + Encoding.UTF8.GetByteCount(DynamoDbTransactionSizeAccountingForTest.ExpectedCondition);
        Assert.Equal(expected, measured.Bytes);
        Assert.True(measured.Bytes > DynamoDbWriteOperationSizeMeasurement.MaximumTransactionBytes);
    }

    [Fact]
    public void NonStorageRepresentationIsUnavailableForBothNewCapabilities()
    {
        var context = CreateContext(CreateSerializedEvent([], tags: [])) with
        {
            Representation = ExecutorSizeRepresentation.LogicalSerializedEventUtf8
        };

        var max = new DynamoDbMaxWrittenItemSizeMeasurement().Measure(context);
        var operation = new DynamoDbWriteOperationSizeMeasurement().Measure(context);

        Assert.False(max.IsAvailable);
        Assert.False(operation.IsAvailable);
        Assert.Contains("StorageItem", max.Reason);
        Assert.Contains("StorageItem", operation.Reason);
    }

    [Theory]
    [InlineData(false, "dynamodb-max-written-item")]
    [InlineData(true, "dynamodb-max-written-item")]
    [InlineData(false, "dynamodb-write-operation")]
    [InlineData(true, "dynamodb-write-operation")]
    public async Task NewCapabilityUnavailableBehaviorIsStrictOrDiagnosticWithoutChangingWrites(
        bool strict,
        string scope)
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CoreInMemoryEventStore(domain.EventTypes);
        IExecutorSizeMeasurement measurement = scope == DynamoDbMaxWrittenItemSizeMeasurement.Scope
            ? new DynamoDbMaxWrittenItemSizeMeasurement()
            : new DynamoDbWriteOperationSizeMeasurement();
        var policy = new ExecutorSizePolicy(
            scope,
            ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
            maxBytesPerEvent: 1024,
            strictness: strict ? ExecutorSizeStrictness.Strict : ExecutorSizeStrictness.NonStrict,
            measurement: measurement);
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(store, domain),
            domain,
            new ExecutorSizeGateOptions().Add(policy));

        var result = await executor.CommitSerializableEventsAsync(
            new SerializedCommitRequest([CreateCandidate([], "capability")], []));

        if (strict)
        {
            var exception = Assert.IsType<ExecutorSizeCapabilityException>(result.GetException());
            Assert.Equal(scope, exception.Scope);
            Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
        }
        else
        {
            Assert.True(result.IsSuccess);
            var diagnostic = Assert.Single(result.GetValue().SizeGateDiagnostics);
            Assert.Equal(scope, diagnostic.Scope);
            Assert.Contains("StorageItem", diagnostic.Reason);
            Assert.Single((await store.ReadAllSerializableEventsAsync()).GetValue());
        }
    }

    [Fact]
    public async Task MaxWrittenItemPolicyRejectsBeforeAnyDynamoDbDispatch()
    {
        var domain = DomainType.GetDomainTypes();
        var client = System.Reflection.DispatchProxy.Create<IAmazonDynamoDB,
            DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb>();
        var capture = (DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb)(object)client;
        var store = NewStore(client, domain, "max-dispatch");
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(new CoreInMemoryEventStore(domain.EventTypes), domain),
            domain,
            new ExecutorSizeGateOptions().AddDynamoDbMaxWrittenItemPolicy());

        var oversized = CreateCandidate(
            Enumerable.Repeat((byte)'x', (int)DynamoDbMaxWrittenItemSizeMeasurement.MaximumItemBytes).ToArray(),
            "oversized");
        var result = await executor.CommitSerializableEventsAsync(
            new SerializedCommitRequest([oversized], []));

        var exception = Assert.IsType<ExecutorSizeLimitExceededException>(result.GetException());
        Assert.Equal(DynamoDbMaxWrittenItemSizeMeasurement.Scope, exception.Scope);
        Assert.False(exception.IsOperationLimit);
        Assert.Equal(0, capture.WriteDispatches);
        Assert.Empty(capture.EventItems);
        Assert.Empty(capture.TagItems);
    }

    [Fact]
    public async Task WriteOperationPolicyRejectsLastEventBeforeAnyDynamoDbDispatch()
    {
        var domain = DomainType.GetDomainTypes();
        var client = System.Reflection.DispatchProxy.Create<IAmazonDynamoDB,
            DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb>();
        var capture = (DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb)(object)client;
        var store = NewStore(client, domain, "operation-dispatch");
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(new CoreInMemoryEventStore(domain.EventTypes), domain),
            domain,
            new ExecutorSizeGateOptions().AddDynamoDbWriteOperationPolicy());

        var first = CreateCandidate([], "first");
        var oversized = CreateCandidate(
            Enumerable.Repeat((byte)'x', 4 * 1024 * 1024).ToArray(),
            "oversized");
        var result = await executor.CommitSerializableEventsAsync(
            new SerializedCommitRequest([first, oversized], []));

        var exception = Assert.IsType<ExecutorSizeLimitExceededException>(result.GetException());
        Assert.Equal(DynamoDbWriteOperationSizeMeasurement.Scope, exception.Scope);
        Assert.True(exception.IsOperationLimit);
        Assert.InRange(exception.EventIndex, 0, 1);
        Assert.Equal(0, capture.WriteDispatches);
        Assert.Empty(capture.EventItems);
        Assert.Empty(capture.TagItems);
    }

    [Fact]
    public async Task WholeOperationBudgetRejectsAggregateEvenWhenIndividualItemsAreLegal()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CoreInMemoryEventStore(domain.EventTypes);
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(store, domain),
            domain,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                DynamoDbWriteOperationSizeMeasurement.Scope,
                ExecutorSizeRepresentation.StorageItem,
                maxBytesPerOperation: 100,
                measurement: new FixedMeasurement(60))));

        var result = await executor.CommitSerializableEventsAsync(
            new SerializedCommitRequest([CreateCandidate([], "one"), CreateCandidate([], "two")], []));

        var exception = Assert.IsType<ExecutorSizeLimitExceededException>(result.GetException());
        Assert.True(exception.IsOperationLimit);
        Assert.Equal(120, exception.MeasuredBytes);
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    private static SerializableEvent CreateTagLargestBoundary(
        DynamoDbMaxWrittenItemSizeMeasurement measurement,
        long expectedBytes,
        string serviceId)
    {
        for (var typeLength = 1; typeLength <= 256; typeLength++)
        {
            var eventType = "T" + new string('e', typeLength - 1);
            var seed = CreateSerializedEvent([], ["Tag:"], eventType);
            var low = 0;
            var high = 500_000;
            while (low <= high)
            {
                var suffixLength = low + ((high - low) / 2);
                var candidate = seed with { Tags = ["Tag:" + new string('x', suffixLength)] };
                var measured = Measure(measurement, candidate, serviceId);
                if (measured == expectedBytes)
                    return candidate;

                if (measured < expectedBytes)
                    low = suffixLength + 1;
                else
                    high = suffixLength - 1;
            }
        }

        throw new Xunit.Sdk.XunitException($"Could not construct a tag boundary of {expectedBytes} bytes.");
    }

    private static List<SerializableEvent> CreateOperationBoundary(
        DynamoDbWriteOperationSizeMeasurement measurement,
        long expectedBytes)
    {
        const int fixedEventCount = 10;
        const int fixedPayloadLength = 380_000;
        var events = Enumerable.Range(0, fixedEventCount)
            .Select(_ => CreateSerializedEvent(
                Enumerable.Repeat((byte)'x', fixedPayloadLength).ToArray(),
                [],
                "OperationEvent"))
            .ToList();
        var used = events.Sum(serialized => Measure(measurement, serialized));
        var emptyLast = CreateSerializedEvent([], [], "OperationEvent");
        var lastPayloadLength = checked((int)(expectedBytes - used - Measure(measurement, emptyLast)));
        Assert.InRange(lastPayloadLength, 0, (int)DynamoDbMaxWrittenItemSizeMeasurement.MaximumItemBytes);
        events.Add(emptyLast with
        {
            Payload = Enumerable.Repeat((byte)'x', lastPayloadLength).ToArray()
        });
        return events;
    }

    private static long Measure(
        IExecutorSizeMeasurement measurement,
        SerializableEvent serialized,
        string serviceId = "operation-test")
    {
        var result = measurement.Measure(CreateContext(serialized, serviceId));
        Assert.True(result.IsAvailable, result.Reason);
        return Assert.IsType<long>(result.Bytes);
    }

    private static DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb CaptureSerializedWrite(
        SerializableEvent serialized,
        string serviceId)
    {
        var domain = DomainType.GetDomainTypes();
        var client = System.Reflection.DispatchProxy.Create<IAmazonDynamoDB,
            DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb>();
        var capture = (DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb)(object)client;
        Assert.True(NewStore(client, domain, serviceId).WriteSerializableEventsAsync([serialized]).Result.IsSuccess);
        return capture;
    }

    private static DynamoDbEventStore NewStore(
        IAmazonDynamoDB client,
        DcbDomainTypes domain,
        string serviceId,
        int maxTransactionItems = 100,
        IEventTypes? eventTypes = null)
    {
        var options = Options.Create(new DynamoDbEventStoreOptions
        {
            AutoCreateTables = false,
            EventsTableName = $"events-{serviceId}",
            TagsTableName = $"tags-{serviceId}",
            ProjectionStatesTableName = $"projection-{serviceId}",
            MaxTransactionItems = maxTransactionItems
        });
        return new DynamoDbEventStore(
            new DynamoDbContext(client, options),
            eventTypes ?? domain.EventTypes,
            new FixedServiceIdProvider(serviceId));
    }

    private static ExecutorSizeMeasurementContext CreateContext(
        SerializableEvent serialized,
        string serviceId = "operation-test") =>
        new(
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
            null);

    private static SerializableEvent CreateSerializedEvent(
        byte[] payload,
        IReadOnlyList<string> tags,
        string eventType = nameof(StudentCreated)) =>
        new(
            payload,
            SortableUniqueId.GenerateNew(),
            Guid.CreateVersion7(),
            new EventMetadata("cause", "correlation", "user"),
            tags.ToList(),
            eventType);

    private static SerializableEventCandidate CreateCandidate(byte[] sizeHint, string tag)
    {
        var summary = sizeHint.Length == 0 ? tag : new string('x', sizeHint.Length);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new WeatherForecastCreated(
                Guid.CreateVersion7(),
                "Tokyo",
                new DateOnly(2026, 9, 10),
                21,
                summary),
            DomainType.GetDomainTypes().JsonSerializerOptions);
        return new SerializableEventCandidate(
            payload,
            nameof(WeatherForecastCreated),
            [$"WeatherForecast:{tag}"]);
    }

    private static Dictionary<string, string> Normalize(IReadOnlyDictionary<string, AttributeValue> item) =>
        item
            .Where(pair => pair.Key is not "pk" and not "sk" and not "eventId" and not "timestamp" and not "createdAt")
            .ToDictionary(pair => pair.Key, pair => ValueSignature(pair.Value), StringComparer.Ordinal);

    private static long IndependentItemBytes(IReadOnlyDictionary<string, AttributeValue> item) =>
        item.Sum(pair => Encoding.UTF8.GetByteCount(pair.Key) + IndependentValueBytes(pair.Value));

    private static long IndependentValueBytes(AttributeValue value)
    {
        if (value.S is not null)
            return Encoding.UTF8.GetByteCount(value.S);
        if (value.N is not null)
            return Encoding.UTF8.GetByteCount(value.N);
        if (value.L is not null)
            return 3 + value.L.Sum(item => 1 + IndependentValueBytes(item));
        if (value.M is not null)
            return 3 + value.M.Sum(pair => 1 + Encoding.UTF8.GetByteCount(pair.Key) + IndependentValueBytes(pair.Value));
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

    private static string ValueSignature(AttributeValue value) =>
        value.S is not null
            ? $"S:{value.S}"
            : value.L is not null
                ? $"L:[{string.Join(",", value.L.Select(ValueSignature))}]"
                : value.N is not null
                    ? $"N:{value.N}"
                    : "other";

    private sealed class FixedServiceIdProvider(string serviceId) : IServiceIdProvider
    {
        public string GetCurrentServiceId() => serviceId;
    }

    private sealed class FixedMeasurement(long bytes) : IExecutorSizeMeasurement
    {
        public ExecutorSizeMeasurementResult Measure(ExecutorSizeMeasurementContext context) =>
            ExecutorSizeMeasurementResult.Exact(bytes);
    }

    private static class DynamoDbTransactionSizeAccountingForTest
    {
        public const string ExpectedCondition = "attribute_not_exists(pk)";
    }
}
