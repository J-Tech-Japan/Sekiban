using System.Text;
using Dcb.Domain;
using Dcb.Domain.Student;
using Dcb.Domain.Weather;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.Tests.Cosmos;

public sealed class CosmosEventDocumentSizeGateTests
{
    private const string ServiceId = "g72-test";

    [Fact]
    public void Mapper_PreservesIdentityMetadataTagsAndUtcTimestampAcrossAllAdapters()
    {
        var domain = DomainType.GetDomainTypes();
        var @event = NewEvent(domain, "東京😀", ["Student:東京😀", "タグ"]);
        var serialized = @event.ToSerializableEvent(domain.EventTypes);
        var timestamp = new DateTime(2042, 3, 4, 5, 6, 7, DateTimeKind.Utc).AddTicks(1_234_567);

        var typed = CosmosEventDocumentMapper.FromEvent(
            @event,
            domain.EventTypes.SerializeEventPayload(@event.Payload),
            ServiceId,
            timestamp);
        var serializedRoute = CosmosEventDocumentMapper.FromSerializableEvent(serialized, ServiceId, timestamp);
        var parts = CosmosEventDocumentMapper.FromParts(
            ServiceId,
            @event.Id,
            @event.SortableUniqueIdValue,
            @event.EventType,
            Encoding.UTF8.GetString(serialized.Payload),
            serialized.Tags,
            serialized.EventMetadata,
            timestamp);
        var publicFactory = CosmosEvent.FromEvent(
            @event,
            typed.Payload,
            ServiceId);

        Assert.Equal(typed.Pk, serializedRoute.Pk);
        Assert.Equal(typed.Pk, parts.Pk);
        Assert.Equal(typed.Pk, publicFactory.Pk);
        Assert.Equal(typed.Id, serializedRoute.Id);
        Assert.Equal(typed.Id, publicFactory.Id);
        Assert.Equal(typed.SortableUniqueId, serializedRoute.SortableUniqueId);
        Assert.Equal(typed.EventType, serializedRoute.EventType);
        Assert.Equal(typed.Payload, serializedRoute.Payload);
        Assert.Equal(typed.Tags, serializedRoute.Tags);
        Assert.Equal(typed.CausationId, serializedRoute.CausationId);
        Assert.Equal(typed.CorrelationId, serializedRoute.CorrelationId);
        Assert.Equal(typed.ExecutedUser, serializedRoute.ExecutedUser);
        Assert.Equal(timestamp, typed.Timestamp);
        Assert.Equal(DateTimeKind.Utc, typed.Timestamp.Kind);

        var compatibilityEvent = new Event(
            @event.Payload,
            string.Empty,
            string.Empty,
            @event.Id,
            @event.EventMetadata,
            @event.Tags);
        var compatibilityDocument = CosmosEvent.FromEvent(
            compatibilityEvent,
            typed.Payload,
            ServiceId);
        Assert.Equal(string.Empty, compatibilityDocument.SortableUniqueId);
        Assert.Equal(string.Empty, compatibilityDocument.EventType);

        Assert.Throws<ArgumentException>(() => CosmosEventDocumentMapper.FromParts(
            ServiceId,
            @event.Id,
            @event.SortableUniqueIdValue,
            @event.EventType,
            typed.Payload,
            typed.Tags,
            @event.EventMetadata,
            new DateTime(2042, 3, 4, 5, 6, 7, DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(() => CosmosEventDocumentMapper.FromParts(
            ServiceId,
            @event.Id,
            @event.SortableUniqueIdValue,
            @event.EventType,
            typed.Payload,
            typed.Tags,
            @event.EventMetadata,
            new DateTime(2042, 3, 4, 5, 6, 7, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void ProviderSerializer_ThroughCosmosClientOptions_DisposesResponseStream()
    {
        var document = new CosmosEvent
        {
            Pk = "g72-test|boundary",
            ServiceId = ServiceId,
            Id = "boundary",
            SortableUniqueId = "000000000000000000000000000",
            EventType = nameof(StudentCreated),
            Payload = "{\"studentId\":\"boundary\"}",
            Tags = ["Student:boundary"],
            Timestamp = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            CausationId = "cause",
            CorrelationId = "correlation",
            ExecutedUser = "user"
        };
        var serializer = new CosmosProviderDefaultSerializer();
        using var client = new CosmosClient(
            "AccountEndpoint=https://localhost:8081/;AccountKey=" +
            "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
            new CosmosClientOptions
            {
                ConnectionMode = ConnectionMode.Gateway,
                LimitToEndpoint = true,
                ConsistencyLevel = ConsistencyLevel.Session,
                Serializer = serializer
            });

        Assert.Same(serializer, client.ClientOptions.Serializer);
        using var stream = serializer.ToStream(document);
        var response = client.ClientOptions.Serializer!.FromStream<CosmosEvent>(stream);

        Assert.Equal(document.Id, response.Id);
        Assert.Equal(document.Payload, response.Payload);
        Assert.Equal(document.Timestamp, response.Timestamp);
        Assert.False(stream.CanRead);
    }

    [Fact]
    public void ProviderSerializer_FromStreamStream_RetainsSdkPassthroughAtPublicContainerBoundary()
    {
        var serializer = new CosmosProviderDefaultSerializer();
        using var client = new CosmosClient(
            "AccountEndpoint=https://localhost:8081/;AccountKey=" +
            "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
            new CosmosClientOptions
            {
                ConnectionMode = ConnectionMode.Gateway,
                LimitToEndpoint = true,
                Serializer = serializer
            });
        var container = client.GetContainer("g72-db", "events");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        var returned = client.ClientOptions.Serializer!.FromStream<Stream>(stream);

        Assert.Equal("events", container.Id);
        Assert.Same(stream, returned);
    }

    [Fact]
    public void ProviderSerializer_FromStream_DisposesTheInputStream()
    {
        var document = new CosmosEvent
        {
            Pk = "g72-test|dispose",
            ServiceId = ServiceId,
            Id = "dispose",
            SortableUniqueId = "suid",
            EventType = nameof(StudentCreated),
            Payload = "{}",
            Tags = ["Student:dispose"],
            Timestamp = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };
        using var stream = new CosmosProviderDefaultSerializer().ToStream(document);

        var restored = new CosmosProviderDefaultSerializer().FromStream<CosmosEvent>(stream);

        Assert.Equal(document.Id, restored.Id);
        Assert.False(stream.CanRead);
    }

    [Fact]
    public void ProviderSerializer_PreservesUtcBytesForAllCurrentCosmosDocumentWriters()
    {
        var utc = new DateTime(2042, 3, 4, 5, 6, 7, DateTimeKind.Utc).AddTicks(1_234_567);
        var documents = new object[]
        {
            new CosmosEvent
            {
                Pk = "g72-test|event",
                ServiceId = ServiceId,
                Id = "event",
                SortableUniqueId = "000000000000000000000000000",
                EventType = nameof(StudentCreated),
                Payload = "{\"studentId\":\"event\"}",
                Tags = ["Student:event", "タグ"],
                Timestamp = utc,
                CausationId = "cause",
                CorrelationId = "correlation",
                ExecutedUser = "user"
            },
            new CosmosTag
            {
                Pk = "g72-test|Student:event",
                ServiceId = ServiceId,
                Id = "event-tag",
                Tag = "Student:event",
                TagGroup = "Student",
                EventType = nameof(StudentCreated),
                SortableUniqueId = "000000000000000000000000000",
                EventId = "event",
                CreatedAt = utc
            },
            new CosmosMultiProjectionState
            {
                DocumentType = "projectionState",
                Pk = "g72-test|projection",
                ServiceId = ServiceId,
                Id = "projection-v1",
                PartitionKey = "projection",
                ProjectorName = "projection",
                ProjectorVersion = "v1",
                PayloadType = "state",
                LastSortableUniqueId = "000000000000000000000000000",
                EventsProcessed = 3,
                StateData = "AQID",
                IsOffloaded = false,
                OriginalSizeBytes = 3,
                CompressedSizeBytes = 3,
                SafeWindowThreshold = "000000000000000000000000000",
                CreatedAt = utc,
                UpdatedAt = utc,
                BuildSource = "test",
                BuildHost = "host",
                Generation = 1,
                Lifecycle = 0,
                ClusterId = "cluster",
                ActivationId = "activation",
                Sequence = 2,
                AppliedEventCount = 3,
                LastAppliedSortableUniqueId = "000000000000000000000000000",
                LastTraversedSortableUniqueId = "000000000000000000000000000",
                RecordedAtUtc = new DateTimeOffset(utc),
                Phase = "active",
                LeaseExpiresAtUtc = new DateTimeOffset(utc.AddMinutes(1)),
                IsFaulted = false,
                SwitchKind = "ordinary",
                SwitchReason = "test",
                SwitchedAtUtc = new DateTimeOffset(utc)
            }
        };

        foreach (var document in documents)
        {
            var providerBytes = ProviderSerializerBytes(document);
            var previousSdkBytes = PreviousSdkCamelCaseSerializerBytes(document);
            Assert.Equal(previousSdkBytes, providerBytes);
        }
    }

    [Fact]
    public void ProviderSerializer_MatchesSdkCamelCaseForExplicitNamesAndDictionaryKeys()
    {
        var document = new SerializerNameParityFixture
        {
            ExplicitName = "explicit",
            PlainName = "plain",
            DictionaryValues = new Dictionary<string, string> { ["KeyOne"] = "value" }
        };

        var providerBytes = ProviderSerializerBytes(document);
        var sdkBytes = PreviousSdkCamelCaseSerializerBytes(document);

        Assert.Equal(sdkBytes, providerBytes);
        var json = Encoding.UTF8.GetString(providerBytes);
        Assert.Contains("\"explicitName\"", json, StringComparison.Ordinal);
        Assert.Contains("\"keyOne\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypedWriterSerializesPayloadExactlyOnce_AndAllWritersUseTheSameDocumentShape()
    {
        var domain = DomainType.GetDomainTypes();
        var @event = NewEvent(domain, "shape");
        var counting = new CountingEventTypes(domain.EventTypes);
        var typed = NewStore(counting, new InMemoryCosmosClient(), "typed");

        var typedResult = await typed.Store.WriteEventsAsync([@event]);

        Assert.True(typedResult.IsSuccess);
        Assert.Equal(1, counting.SerializeCalls);
        var typedDocument = typed.Client.Container(typed.Options.EventsContainerName).Items.Single();

        var serialized = @event.ToSerializableEvent(domain.EventTypes);
        var serializedLineage = NewStore(domain.EventTypes, new InMemoryCosmosClient(), "serialized");
        var serializedResult = await serializedLineage.Store.WriteSerializableEventsAsync([serialized]);
        Assert.True(serializedResult.IsSuccess);
        var serializedDocument = serializedLineage.Client
            .Container(serializedLineage.Options.EventsContainerName).Items.Single();

        var conditionalLineage = NewStore(domain.EventTypes, new InMemoryCosmosClient(), "conditional");
        var conditionalResult = await conditionalLineage.Store.AppendIfUniqueAsync(
            new ConditionalAppendRequest("shape-operation", serialized));
        Assert.True(conditionalResult.IsSuccess);
        var conditionalDocument = conditionalLineage.Client
            .Container(conditionalLineage.Options.EventsContainerName).Items.Single();

        Assert.Equal(typedDocument["serviceId"]?.ToObject<string>(), serializedDocument["serviceId"]?.ToObject<string>());
        Assert.Equal(typedDocument["sortableUniqueId"]?.ToObject<string>(), serializedDocument["sortableUniqueId"]?.ToObject<string>());
        Assert.Equal(typedDocument["eventType"]?.ToObject<string>(), conditionalDocument["eventType"]?.ToObject<string>());
        Assert.Equal(typedDocument["payload"]?.ToObject<string>(), serializedDocument["payload"]?.ToObject<string>());
        Assert.Equal(typedDocument["payload"]?.ToObject<string>(), conditionalDocument["payload"]?.ToObject<string>());
        Assert.Equal(typedDocument["tags"]?.ToString(), serializedDocument["tags"]?.ToString());
        Assert.Equal(typedDocument["tags"]?.ToString(), conditionalDocument["tags"]?.ToString());
        Assert.Equal(typedDocument["causationId"]?.ToObject<string>(), conditionalDocument["causationId"]?.ToObject<string>());
        Assert.Equal(typedDocument["correlationId"]?.ToObject<string>(), conditionalDocument["correlationId"]?.ToObject<string>());
        Assert.Equal(typedDocument["executedUser"]?.ToObject<string>(), conditionalDocument["executedUser"]?.ToObject<string>());
    }

    [Fact]
    public async Task FirstTypedSerializationFailureIsReportedOnce_WithoutASecondPayloadSerialization()
    {
        var domain = DomainType.GetDomainTypes();
        var counting = new CountingEventTypes(domain.EventTypes) { ThrowOnFirstSerialize = true };
        var client = new InMemoryCosmosClient();
        var lineage = NewStore(counting, client, "serialize-failure");

        var result = await lineage.Store.WriteEventsAsync([NewEvent(domain, "failure")]);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, counting.SerializeCalls);
        Assert.Empty(client.Container(lineage.Options.EventsContainerName).Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_000)]
    [InlineData(1_230_000)]
    [InlineData(1_234_567)]
    public void CertifiedBound_CoversProviderSerializationForFractionAndYearBoundaries(int fractionalTicks)
    {
        var domain = DomainType.GetDomainTypes();
        var @event = NewEvent(domain, "ASCII 東京😀 \"quoted\"", ["tag:null", "タグ"]);
        var serialized = @event.ToSerializableEvent(domain.EventTypes);
        var context = NewOwnedContext();
        var measurement = new CosmosEventDocumentSizeMeasurement(context);
        var sentinelDocument = CosmosEventDocumentMapper.FromSerializableEvent(
            serialized,
            ServiceId,
            CosmosEventDocumentMapper.MeasurementTimestampUtc);
        var measured = measurement.Measure(new ExecutorSizeMeasurementContext(
            CosmosEventDocumentSizeMeasurement.Scope,
            ExecutorSizeRepresentation.StorageItem,
            @event,
            serialized,
            ServiceId,
            null,
            null));

        Assert.True(measured.IsAvailable, measured.Reason);
        Assert.Null(measured.Bytes);
        var bound = Assert.IsType<long>(measured.CertifiedUpperBound);
        using (var sentinelStream = new CosmosProviderDefaultSerializer().ToStream(sentinelDocument))
        {
            Assert.Equal(sentinelStream.Length + 8, bound);
        }

        var maxObservedLength = 0L;
        foreach (var timestamp in new[]
                 {
                     new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(fractionalTicks),
                     new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(fractionalTicks),
                     new DateTime(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc).AddTicks(fractionalTicks)
                 })
        {
            var document = CosmosEventDocumentMapper.FromParts(
                ServiceId,
                @event.Id,
                @event.SortableUniqueIdValue,
                @event.EventType,
                Encoding.UTF8.GetString(serialized.Payload),
                serialized.Tags,
                serialized.EventMetadata,
                timestamp);
            using var stream = new CosmosProviderDefaultSerializer().ToStream(document);
            maxObservedLength = Math.Max(maxObservedLength, stream.Length);
            var slack = bound - stream.Length;
            Assert.True(slack >= 0,
                $"timestamp={timestamp:o}, bound={bound}, actual={stream.Length}, slack={slack}");
        }

        var expectedMaxDocument = CosmosEventDocumentMapper.FromParts(
            ServiceId,
            @event.Id,
            @event.SortableUniqueIdValue,
            @event.EventType,
            Encoding.UTF8.GetString(serialized.Payload),
            serialized.Tags,
            serialized.EventMetadata,
            new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(fractionalTicks));
        using var expectedMaxStream = new CosmosProviderDefaultSerializer().ToStream(expectedMaxDocument);
        Assert.True(maxObservedLength <= bound - 8,
            $"bound={bound}, maxObserved={maxObservedLength}, sentinelSlack=8");
        Assert.Equal(expectedMaxStream.Length, maxObservedLength);
    }

    [Fact]
    public async Task ProviderOwnedMeasurementCreatesOneClientConcurrently_AndDisposePreventsResurrection()
    {
        var domain = DomainType.GetDomainTypes();
        var context = NewOwnedContext();
        var measurement = new CosmosEventDocumentSizeMeasurement(context);
        var measurementContext = CreateMeasurementContext(domain);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => Task.Run(() => measurement.Measure(measurementContext))));

        Assert.All(results, result => Assert.True(result.IsAvailable, result.Reason));
        Assert.Equal(1, context.ClientCreationCount);

        context.Dispose();

        var afterDispose = measurement.Measure(measurementContext);
        Assert.False(afterDispose.IsAvailable);
        Assert.Contains("disposed", afterDispose.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, context.ClientCreationCount);
    }

    [Fact]
    public void MeasurementRejectsNonStorageRepresentationAndPreservesNamedCapabilityReasons()
    {
        var domain = DomainType.GetDomainTypes();
        var pair = CreateMeasurementContext(domain);
        var measurement = new CosmosEventDocumentSizeMeasurement(NewOwnedContext());

        var logical = measurement.Measure(pair with
        {
            Representation = ExecutorSizeRepresentation.LogicalSerializedEventUtf8
        });
        Assert.False(logical.IsAvailable);
        Assert.Contains("only the StorageItem representation", logical.Reason);

        var injected = new CosmosEventDocumentSizeMeasurement(new CosmosDbContext(new InMemoryCosmosClient()));
        var injectedResult = injected.Measure(pair);
        Assert.False(injectedResult.IsAvailable);
        Assert.Contains("injected clients are unproven", injectedResult.Reason);

        using var provider = new ServiceCollection()
            .AddSekibanDcbCosmosEventDocumentSizeGate(123)
            .BuildServiceProvider();
        var missingContextPolicy = Assert.Single(
            provider.GetRequiredService<ExecutorSizeGateOptions>().Policies);
        var missingContextResult = missingContextPolicy.Measurement!.Measure(pair);
        Assert.False(missingContextResult.IsAvailable);
        Assert.Contains("CosmosDbContext is not registered", missingContextResult.Reason);
    }

    [Fact]
    public void CosmosRegistrationValidatesEagerlyAndKeepsTheExistingContextSingletonPath()
    {
        var options = new ExecutorSizeGateOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            options.AddCosmosEventDocumentPolicy(NewOwnedContext(), 0));
        Assert.Empty(options.Policies);

        var services = new ServiceCollection();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddSekibanDcbCosmosEventDocumentSizeGate(2_000_001));
        Assert.Empty(services);

        var context = NewOwnedContext();
        using var provider = new ServiceCollection()
            .AddSingleton(context)
            .AddSekibanDcbCosmosEventDocumentSizeGate(321)
            .BuildServiceProvider();
        var policy = Assert.Single(provider.GetRequiredService<ExecutorSizeGateOptions>().Policies);
        Assert.Same(context, provider.GetRequiredService<CosmosDbContext>());
        Assert.Equal(321, policy.MaxBytesPerEvent);
        Assert.Equal(ExecutorSizeStrictness.Strict, policy.Strictness);
    }

    [Fact]
    public void CosmosRegistrationRejectsCoreMixingBeforeAddingDescriptors()
    {
        var providerFirst = new ServiceCollection();
        providerFirst.AddSekibanDcbCosmosEventDocumentSizeGate();
        var providerCount = providerFirst.Count;

        Assert.Throws<InvalidOperationException>(() =>
            providerFirst.AddSekibanDcbExecutorSizeGate(_ => { }));
        Assert.Equal(providerCount, providerFirst.Count);

        var coreFirst = new ServiceCollection();
        coreFirst.AddSekibanDcbExecutorSizeGate(_ => { });
        var coreCount = coreFirst.Count;

        Assert.Throws<InvalidOperationException>(() =>
            coreFirst.AddSekibanDcbCosmosEventDocumentSizeGate());
        Assert.Equal(coreCount, coreFirst.Count);
    }

    [Fact]
    public async Task ProviderOwnedCertifiedBoundRejectsBeforeDispatchOnAllFiveWriteEntrances()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CosmosGateOnlyStore();
        var publisher = new RecordingEventPublisher();
        var context = NewOwnedContext();
        var options = new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
            CosmosEventDocumentSizeMeasurement.Scope,
            ExecutorSizeRepresentation.StorageItem,
            maxBytesPerEvent: 1,
            measurement: new CosmosEventDocumentSizeMeasurement(context)));
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(store, domain),
            domain,
            options,
            publisher);

        var typed = await executor.ExecuteAsync(
            new GateCommand(Guid.CreateVersion7()),
            HandleGateCommand);
        var serialized = await executor.CommitSerializableEventsAsync(
            new SerializedCommitRequest([CreateCandidate(domain)], []));
        var expectedPosition = await executor.CommitSerializableEventsWithExpectedTagPositionsAsync(
            new VersionedExpectedTagPositionSerializedCommitRequest(
                VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion,
                [CreateCandidate(domain)],
                [],
                []));
        var conditional = await executor.ExecuteAsync(
            new GateCommand(Guid.CreateVersion7()),
            HandleGateCommand,
            new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification("g72-conditional") });
        var serializedConditional = await executor.CommitSerializableEventConditionallyAsync(
            new SerializedConditionalCommitRequest(
                SerializedConditionalCommitRequest.CurrentVersion,
                CreateCandidate(domain),
                "g72-serialized-conditional"));

        Assert.All(
            new[]
            {
                typed.GetException(),
                serialized.GetException(),
                expectedPosition.GetException(),
                conditional.GetException(),
                serializedConditional.GetException()
            },
            exception => Assert.IsType<ExecutorSizeLimitExceededException>(exception));
        Assert.Equal(0, store.WriteCalls);
        Assert.Equal(0, store.ConditionalAppendCalls);
        Assert.Equal(0, store.ExpectedPositionWriteCalls);
        Assert.Empty(publisher.PublishedEvents);
        Assert.Equal(1, context.ClientCreationCount);
    }

    [Fact]
    public async Task ProviderOwnedCertifiedBound_AllowsUnderQuotaAndPublishesOnce()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new Sekiban.Dcb.Testing.InMemoryEventStore(domain.EventTypes);
        var publisher = new RecordingEventPublisher();
        var context = NewOwnedContext();
        var options = new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
            CosmosEventDocumentSizeMeasurement.Scope,
            ExecutorSizeRepresentation.StorageItem,
            maxBytesPerEvent: 2_000_000,
            measurement: new CosmosEventDocumentSizeMeasurement(context)));
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(store, domain),
            domain,
            options,
            publisher);

        var result = await executor.ExecuteAsync(
            new GateCommand(Guid.CreateVersion7()),
            HandleGateCommand);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        var published = Assert.Single(publisher.PublishedEvents);
        Assert.IsType<WeatherForecastCreated>(published.Event.Payload);
    }

    [Fact]
    public async Task ProviderOwnedCertifiedBoundRejectsBeforeRecordedStoreDispatch()
    {
        var domain = DomainType.GetDomainTypes();
        var inner = new Sekiban.Dcb.Testing.InMemoryEventStore(domain.EventTypes);
        var recording = new RecordingEventStore(inner);
        var context = NewOwnedContext();
        var probe = new CosmosEventDocumentSizeMeasurement(context).Measure(CreateMeasurementContext(domain));
        Assert.True(probe.IsAvailable, probe.Reason);
        var executor = new GeneralSekibanExecutor(
            recording,
            new InMemoryObjectAccessor(recording, domain),
            domain,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                CosmosEventDocumentSizeMeasurement.Scope,
                ExecutorSizeRepresentation.StorageItem,
                maxBytesPerEvent: 1,
                measurement: new CosmosEventDocumentSizeMeasurement(context))));

        var result = await executor.CommitSerializableEventsAsync(
            new SerializedCommitRequest([CreateCandidate(domain)], []));

        var exception = Assert.IsType<ExecutorSizeLimitExceededException>(result.GetException());
        Assert.True(exception.IsCertifiedUpperBound);
        Assert.Equal(0, recording.SerializedWriteCalls);
        Assert.Equal(1, context.ClientCreationCount);
    }

    [Theory]
    [InlineData(ExecutorSizeStrictness.Strict)]
    [InlineData(ExecutorSizeStrictness.NonStrict)]
    public async Task InjectedClientIsUnavailableWithStrictAndNonStrictOutcomes(ExecutorSizeStrictness strictness)
    {
        var domain = DomainType.GetDomainTypes();
        var inner = new Sekiban.Dcb.Testing.InMemoryEventStore(domain.EventTypes);
        var recording = new RecordingEventStore(inner);
        var executor = new GeneralSekibanExecutor(
            recording,
            new InMemoryObjectAccessor(recording, domain),
            domain,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                CosmosEventDocumentSizeMeasurement.Scope,
                ExecutorSizeRepresentation.StorageItem,
                maxBytesPerEvent: 1024,
                strictness: strictness,
                measurement: new CosmosEventDocumentSizeMeasurement(new CosmosDbContext(new InMemoryCosmosClient())))));

        var result = await executor.CommitSerializableEventsAsync(
            new SerializedCommitRequest([CreateCandidate(domain)], []));

        if (strictness == ExecutorSizeStrictness.Strict)
        {
            Assert.IsType<ExecutorSizeCapabilityException>(result.GetException());
            Assert.Equal(0, recording.SerializedWriteCalls);
        }
        else
        {
            Assert.True(result.IsSuccess);
            Assert.Contains(result.GetValue().SizeGateDiagnostics, diagnostic =>
                diagnostic.Scope == CosmosEventDocumentSizeMeasurement.Scope &&
                diagnostic.Reason.Contains("injected clients are unproven", StringComparison.Ordinal));
            Assert.Equal(1, recording.SerializedWriteCalls);
        }
    }

    private static CosmosDbContext NewOwnedContext() =>
        new(
            "AccountEndpoint=https://localhost:8081/;AccountKey=" +
            "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
            "g72-db");

    private static readonly JsonSerializerSettings PreviousSdkCamelCaseSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Include,
        DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind
    };

    private static byte[] ProviderSerializerBytes(object document)
    {
        using var stream = document switch
        {
            CosmosEvent value => new CosmosProviderDefaultSerializer().ToStream(value),
            CosmosTag value => new CosmosProviderDefaultSerializer().ToStream(value),
            CosmosMultiProjectionState value => new CosmosProviderDefaultSerializer().ToStream(value),
            SerializerNameParityFixture value => new CosmosProviderDefaultSerializer().ToStream(value),
            _ => throw new ArgumentOutOfRangeException(nameof(document), document.GetType(), "Unsupported document")
        };
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static byte[] PreviousSdkCamelCaseSerializerBytes(object document) =>
        Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(document, PreviousSdkCamelCaseSettings));

    private static (CosmosDbEventStore Store, InMemoryCosmosClient Client, CosmosDbEventStoreOptions Options) NewStore(
        IEventTypes eventTypes,
        InMemoryCosmosClient client,
        string suffix)
    {
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = $"events-{suffix}",
            TagsContainerName = $"tags-{suffix}",
            WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward,
            TagWriteRetry = new CosmosTagWriteRetryOptions { MaxAttempts = 1, JitterRatio = 0 }
        };
        return (
            new CosmosDbEventStore(
                new CosmosDbContext(client, "g72-db", null, options),
                eventTypes,
                new FixedServiceIdProvider(ServiceId),
                new DefaultCosmosContainerResolver(options)),
            client,
            options);
    }

    private static Event NewEvent(DcbDomainTypes domain, string summary, List<string>? tags = null)
    {
        var id = Guid.CreateVersion7();
        return new Event(
            new StudentCreated(Guid.CreateVersion7(), summary, 21),
            SortableUniqueId.GenerateNew(),
            nameof(StudentCreated),
            id,
            new EventMetadata("cause", "correlation", "user"),
            tags ?? [$"Student:{id}"]);
    }

    private static ExecutorSizeMeasurementContext CreateMeasurementContext(DcbDomainTypes domain)
    {
        var @event = NewEvent(domain, "measurement");
        return new ExecutorSizeMeasurementContext(
            CosmosEventDocumentSizeMeasurement.Scope,
            ExecutorSizeRepresentation.StorageItem,
            @event,
            @event.ToSerializableEvent(domain.EventTypes),
            ServiceId,
            null,
            null);
    }

    private static SerializableEventCandidate CreateCandidate(DcbDomainTypes domain)
    {
        var id = Guid.CreateVersion7();
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new WeatherForecastCreated(id, "Tokyo", new DateOnly(2026, 9, 10), 21, "g72"),
            domain.JsonSerializerOptions);
        return new SerializableEventCandidate(payload, nameof(WeatherForecastCreated), [$"WeatherForecast:{id}"]);
    }

    private sealed record GateCommand(Guid Id) : ICommand;

    private static Task<ResultBox<EventOrNone>> HandleGateCommand(GateCommand command, ICommandContext _) =>
        Task.FromResult(EventOrNone.Event(
            new WeatherForecastCreated(command.Id, "Tokyo", new DateOnly(2026, 9, 10), 21, "g72"),
            new WeatherForecastTag(command.Id)));

    private sealed class SerializerNameParityFixture
    {
        [JsonProperty("ExplicitName")]
        public string ExplicitName { get; init; } = string.Empty;

        public string PlainName { get; init; } = string.Empty;

        public Dictionary<string, string> DictionaryValues { get; init; } = [];
    }

    private sealed class FixedServiceIdProvider(string serviceId) : IServiceIdProvider
    {
        public string GetCurrentServiceId() => serviceId;
    }

    private sealed class CountingEventTypes(IEventTypes inner) : IEventTypes
    {
        public int SerializeCalls { get; private set; }
        public bool ThrowOnFirstSerialize { get; init; }

        public string SerializeEventPayload(IEventPayload payload)
        {
            SerializeCalls++;
            if (ThrowOnFirstSerialize && SerializeCalls == 1)
                throw new InvalidOperationException("counting serializer failure");
            return inner.SerializeEventPayload(payload);
        }

        public IEventPayload? DeserializeEventPayload(string eventTypeName, string json) =>
            inner.DeserializeEventPayload(eventTypeName, json);

        public Type? GetEventType(string eventTypeName) => inner.GetEventType(eventTypeName);
    }

    private sealed class RecordingEventStore(IEventStore inner) : IEventStore
    {
        public int SerializedWriteCalls { get; private set; }

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => inner.ReadTagsAsync(tag);
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => inner.GetLatestTagAsync(tag);
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => inner.TagExistsAsync(tag);
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => inner.GetEventCountAsync(since);
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => inner.GetAllTagsAsync(tagGroup);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            inner.ReadAllSerializableEventsAsync(since);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) =>
            inner.ReadAllSerializableEventsAsync(since, maxCount);
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) => inner.ReadSerializableEventAsync(eventId);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
            inner.ReadSerializableEventsByTagAsync(tag, since);
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
        {
            SerializedWriteCalls++;
            return inner.WriteSerializableEventsAsync(events);
        }

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => inner.GetLatestSortableUniqueIdAsync();
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<(Event Event, IReadOnlyCollection<ITag> Tags)> PublishedEvents { get; } = [];

        public Task PublishAsync(
            IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
            CancellationToken cancellationToken = default)
        {
            PublishedEvents.AddRange(events);
            return Task.CompletedTask;
        }
    }

    private sealed class CosmosGateOnlyStore :
        IEventStore,
        IConditionalEventStore,
        IExpectedTagPositionEventStore,
        IWriteConditionCapabilityProvider
    {
        public int WriteCalls { get; private set; }
        public int ConditionalAppendCalls { get; private set; }
        public int ExpectedPositionWriteCalls { get; private set; }

        public WriteConditionCapabilityDescriptor DescribeWriteConditions() =>
            WriteConditionCapabilityDescriptor.Supporting(
                "CosmosGateOnly",
                WriteConditionKind.SingleEventUniqueKey,
                WriteConditionKind.ExpectedTagPosition);

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) =>
            Task.FromResult(ResultBox.FromValue<IEnumerable<TagStream>>([]));

        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) =>
            Task.FromResult(ResultBox.Error<TagState>(new NotSupportedException()));

        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) =>
            Task.FromResult(ResultBox.FromValue(false));

        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue(0L));

        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) =>
            Task.FromResult(ResultBox.FromValue<IEnumerable<TagInfo>>([]));

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue<IEnumerable<SerializableEvent>>([]));

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount) =>
            Task.FromResult(ResultBox.FromValue<IEnumerable<SerializableEvent>>([]));

        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) =>
            Task.FromResult(ResultBox.Error<SerializableEvent>(new NotSupportedException()));

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue<IEnumerable<SerializableEvent>>([]));

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
        {
            WriteCalls++;
            throw new InvalidOperationException("Cosmos size gate must run before the provider write.");
        }

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
            Task.FromResult(ResultBox.FromValue(string.Empty));

        public Task<ResultBox<ConditionalAppendReceipt>> AppendIfUniqueAsync(
            ConditionalAppendRequest request,
            CancellationToken cancellationToken = default)
        {
            ConditionalAppendCalls++;
            throw new InvalidOperationException("Cosmos size gate must run before conditional append.");
        }

        public Task<ResultBox<bool>> EnsureExpectedTagPositionEnforcementEnabledAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResultBox.FromValue(true));

        public Task<ResultBox<ExpectedTagPositionWriteResult>> WriteSerializableEventsWithExpectedTagPositionsAsync(
            IReadOnlyList<SerializableEvent> events,
            ExpectedTagPositionSpecification specification,
            CancellationToken cancellationToken = default)
        {
            ExpectedPositionWriteCalls++;
            throw new InvalidOperationException("Cosmos size gate must run before expected-position write.");
        }
    }
}
