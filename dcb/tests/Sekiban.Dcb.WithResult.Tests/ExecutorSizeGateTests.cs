using System.Text.Json;
using Dcb.Domain;
using Dcb.Domain.Weather;
using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;

namespace Sekiban.Dcb.Tests;

public sealed class ExecutorSizeGateTests
{
    [Fact]
    public async Task LogicalPolicy_RejectsTypedCommandBeforeAnyDurableEvent()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CoreInMemoryEventStore(domain.EventTypes);
        var executor = CreateExecutor(
            domain,
            store,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "logical-event",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerEvent: 1)));

        var result = await executor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = Guid.NewGuid(),
            Location = "東京",
            Date = new DateOnly(2026, 9, 9),
            TemperatureC = 21,
            Summary = "サイズゲート"
        });

        var exception = Assert.IsType<ExecutorSizeLimitExceededException>(result.GetException());
        Assert.Equal("logical-event", exception.Scope);
        Assert.Equal(ExecutorSizeRepresentation.LogicalSerializedEventUtf8, exception.Representation);
        Assert.True(exception.MeasuredBytes > exception.LimitBytes);
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public async Task MeasurementBoundary_AllowsEqualAndRejectsAbove()
    {
        var domain = DomainType.GetDomainTypes();
        var equalStore = new CoreInMemoryEventStore(domain.EventTypes);
        var equalExecutor = CreateExecutor(
            domain,
            equalStore,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "measured-event",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerEvent: 42,
                measurement: new DelegateMeasurement(_ => ExecutorSizeMeasurementResult.Exact(42)))));

        var equalResult = await equalExecutor.CommitSerializableEventsAsync(
            SingleSerializedRequest(domain, Guid.NewGuid()));

        Assert.True(equalResult.IsSuccess);
        Assert.Single((await equalStore.ReadAllSerializableEventsAsync()).GetValue());

        var aboveStore = new CoreInMemoryEventStore(domain.EventTypes);
        var aboveExecutor = CreateExecutor(
            domain,
            aboveStore,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "measured-event",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerEvent: 41,
                measurement: new DelegateMeasurement(_ => ExecutorSizeMeasurementResult.Exact(42)))));

        var aboveResult = await aboveExecutor.CommitSerializableEventsAsync(
            SingleSerializedRequest(domain, Guid.NewGuid()));

        var exception = Assert.IsType<ExecutorSizeLimitExceededException>(aboveResult.GetException());
        Assert.Equal(42, exception.MeasuredBytes);
        Assert.Equal(41, exception.LimitBytes);
        Assert.Empty((await aboveStore.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public async Task AvailableStorageMeasurement_PassesWhileDestinationLimitRejectsBeforeEnqueue()
    {
        var domain = DomainType.GetDomainTypes();
        var storageStore = new CoreInMemoryEventStore(domain.EventTypes);
        var storageExecutor = CreateExecutor(
            domain,
            storageStore,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "provider-row",
                ExecutorSizeRepresentation.StorageItem,
                maxBytesPerEvent: 32,
                measurement: new DelegateMeasurement(_ => ExecutorSizeMeasurementResult.Exact(32)))));

        var storageResult = await storageExecutor.CommitSerializableEventsAsync(
            SingleSerializedRequest(domain, Guid.NewGuid()));

        Assert.True(storageResult.IsSuccess);
        Assert.Single((await storageStore.ReadAllSerializableEventsAsync()).GetValue());

        var destinationStore = new CoreInMemoryEventStore(domain.EventTypes);
        var publisher = new RecordingDestinationPublisher();
        var destinationExecutor = CreateExecutor(
            domain,
            destinationStore,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "destination",
                ExecutorSizeRepresentation.Destination,
                maxBytesPerEvent: 10,
                measurement: new DelegateMeasurement(context =>
                    ExecutorSizeMeasurementResult.Exact(context.DestinationKey == "destination-2" ? 11 : 8)))),
            publisher);

        var destinationResult = await destinationExecutor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = Guid.NewGuid(),
            Location = "Tokyo",
            Date = new DateOnly(2026, 9, 9),
            TemperatureC = 21
        });

        var destinationException = Assert.IsType<ExecutorSizeLimitExceededException>(destinationResult.GetException());
        Assert.Equal(11, destinationException.MeasuredBytes);
        Assert.Equal("destination-2", destinationException.DestinationKey);
        Assert.Empty((await destinationStore.ReadAllSerializableEventsAsync()).GetValue());
        Assert.Equal(0, publisher.PlannedPublishCalls);
    }

    [Fact]
    public async Task DestinationFanOut_EnforcesEachDestinationIndependently()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CoreInMemoryEventStore(domain.EventTypes);
        var publisher = new RecordingDestinationPublisher();
        var executor = CreateExecutor(
            domain,
            store,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "destination",
                ExecutorSizeRepresentation.Destination,
                maxBytesPerEvent: 10,
                measurement: new DelegateMeasurement(_ => ExecutorSizeMeasurementResult.Exact(10)))),
            publisher);

        var result = await executor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = Guid.NewGuid(),
            Location = "Tokyo",
            Date = new DateOnly(2026, 9, 9),
            TemperatureC = 21
        });

        Assert.True(result.IsSuccess);
        Assert.Single((await store.ReadAllSerializableEventsAsync()).GetValue());
        Assert.Equal(1, publisher.PlannedPublishCalls);
    }

    [Fact]
    public async Task SerializedBatch_PerEventLimitRejectsTheLastEventBeforeAnyWrite()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CoreInMemoryEventStore(domain.EventTypes);
        var first = CreateCandidate(domain, Guid.NewGuid());
        var last = CreateCandidate(domain, Guid.NewGuid(), summary: new string('x', 256));
        var publisher = new RecordingDestinationPublisher();
        var executor = CreateExecutor(
            domain,
            store,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "logical-event",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerEvent: first.Payload.Length,
                measurement: new DelegateMeasurement(context =>
                    ExecutorSizeMeasurementResult.Exact(context.SerializedEvent.Payload.Length)))),
            publisher);

        var result = await executor.CommitSerializableEventsAsync(
            new SerializedCommitRequest(
                [first, last],
                [
                    new ConsistencyTagEntry(first.Tags[0], ""),
                    new ConsistencyTagEntry(last.Tags[0], "")
                ]));

        var exception = Assert.IsType<ExecutorSizeLimitExceededException>(result.GetException());
        Assert.False(exception.IsOperationLimit);
        Assert.Equal(1, exception.EventIndex);
        Assert.Equal(last.Payload.Length, exception.MeasuredBytes);
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
        Assert.Equal(0, publisher.PlannedPublishCalls);
    }

    [Fact]
    public async Task LogicalUtf8MetadataOverheadChangesTheVerdictAtPinnedBytes()
    {
        var domain = DomainType.GetDomainTypes();
        var policy = new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
            "logical-event",
            ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
            maxBytesPerEvent: 1));

        var minimalStore = new CoreInMemoryEventStore(domain.EventTypes);
        var minimalExecutor = CreateExecutor(
            domain,
            minimalStore,
            policy,
            executedUserProvider: new FixedExecutedUserProvider("u"));
        var minimalResult = await minimalExecutor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Location = "Tokyo",
            Date = new DateOnly(2026, 9, 9),
            TemperatureC = 21,
            Summary = "fixed"
        });
        var minimalException = Assert.IsType<ExecutorSizeLimitExceededException>(minimalResult.GetException());
        Assert.Equal(525, minimalException.MeasuredBytes);

        var acceptedStore = new CoreInMemoryEventStore(domain.EventTypes);
        var acceptedExecutor = CreateExecutor(
            domain,
            acceptedStore,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "logical-event",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerEvent: minimalException.MeasuredBytes)),
            executedUserProvider: new FixedExecutedUserProvider("u"));
        var acceptedResult = await acceptedExecutor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Location = "Tokyo",
            Date = new DateOnly(2026, 9, 9),
            TemperatureC = 21,
            Summary = "fixed"
        });
        Assert.True(acceptedResult.IsSuccess);
        Assert.Single((await acceptedStore.ReadAllSerializableEventsAsync()).GetValue());

        var richStore = new CoreInMemoryEventStore(domain.EventTypes);
        var richExecutor = CreateExecutor(
            domain,
            richStore,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "logical-event",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerEvent: minimalException.MeasuredBytes)),
            executedUserProvider: new FixedExecutedUserProvider("user-with-metadata-overhead"));
        var richResult = await richExecutor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Location = "Tokyo",
            Date = new DateOnly(2026, 9, 9),
            TemperatureC = 21,
            Summary = "fixed"
        });

        var richException = Assert.IsType<ExecutorSizeLimitExceededException>(richResult.GetException());
        Assert.True(richException.MeasuredBytes > minimalException.MeasuredBytes);
        Assert.Empty((await minimalStore.ReadAllSerializableEventsAsync()).GetValue());
        Assert.Empty((await richStore.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public async Task SizeRejection_CancelsConsistencyReservations()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CoreInMemoryEventStore(domain.EventTypes);
        var accessor = new InMemoryObjectAccessor(store, domain);
        var executor = CreateExecutor(
            domain,
            store,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "logical-event",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerEvent: 1)),
            actorAccessor: accessor);
        var eventId = Guid.NewGuid();
        var tag = $"WeatherForecast:{eventId}";

        var result = await executor.CommitSerializableEventsAsync(
            new SerializedCommitRequest(
                [CreateCandidate(domain, eventId)],
                [new ConsistencyTagEntry(tag, "")]));

        Assert.IsType<ExecutorSizeLimitExceededException>(result.GetException());
        var actorResult = await accessor.GetActorAsync<GeneralTagConsistentActor>(tag);
        Assert.True(actorResult.IsSuccess);
        Assert.Empty(await actorResult.GetValue().GetActiveReservationsAsync());
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public async Task SerializedBatch_UsesOnePreparedIdentityAndRejectsOperationBeforeWrite()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CoreInMemoryEventStore(domain.EventTypes);
        var executor = CreateExecutor(
            domain,
            store,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "logical-operation",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerOperation: 1)));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var request = new SerializedCommitRequest(
            [CreateCandidate(domain, first), CreateCandidate(domain, second)],
            [new ConsistencyTagEntry($"WeatherForecast:{first}", ""), new ConsistencyTagEntry($"WeatherForecast:{second}", "")]);

        var result = await executor.CommitSerializableEventsAsync(request);

        var exception = Assert.IsType<ExecutorSizeLimitExceededException>(result.GetException());
        Assert.True(exception.IsOperationLimit);
        Assert.Equal(0, exception.EventIndex);
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public async Task StrictStorageAndDestinationCapabilitiesFailClosedBeforeWrite()
    {
        var domain = DomainType.GetDomainTypes();
        var storageStore = new CoreInMemoryEventStore(domain.EventTypes);
        var storageExecutor = CreateExecutor(
            domain,
            storageStore,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "provider-row",
                ExecutorSizeRepresentation.StorageItem,
                maxBytesPerEvent: 1024,
                measurement: new DelegateMeasurement(_ => ExecutorSizeMeasurementResult.Unavailable("provider encoder not installed")))));

        var storageResult = await storageExecutor.CommitSerializableEventsAsync(
            SingleSerializedRequest(domain, Guid.NewGuid()));

        var storageException = Assert.IsType<ExecutorSizeCapabilityException>(storageResult.GetException());
        Assert.Equal(ExecutorSizeRepresentation.StorageItem, storageException.Representation);
        Assert.Empty((await storageStore.ReadAllSerializableEventsAsync()).GetValue());

        var destinationStore = new CoreInMemoryEventStore(domain.EventTypes);
        var destinationExecutor = CreateExecutor(
            domain,
            destinationStore,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "destination",
                ExecutorSizeRepresentation.Destination,
                maxBytesPerEvent: 1024)));

        var destinationResult = await destinationExecutor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = Guid.NewGuid(),
            Location = "Tokyo",
            Date = new DateOnly(2026, 9, 9),
            TemperatureC = 21
        });

        var destinationException = Assert.IsType<ExecutorSizeCapabilityException>(destinationResult.GetException());
        Assert.Equal(ExecutorSizeRepresentation.Destination, destinationException.Representation);
        Assert.Empty((await destinationStore.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public async Task NonStrictUnavailableMeasurement_WritesWithExplicitDiagnostic()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CoreInMemoryEventStore(domain.EventTypes);
        var executor = CreateExecutor(
            domain,
            store,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "provider-row",
                ExecutorSizeRepresentation.StorageItem,
                maxBytesPerEvent: 1024,
                strictness: ExecutorSizeStrictness.NonStrict,
                measurement: new DelegateMeasurement(_ => ExecutorSizeMeasurementResult.Unavailable("not configured")))));

        var result = await executor.CommitSerializableEventsAsync(
            SingleSerializedRequest(domain, Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.Single(result.GetValue().SizeGateDiagnostics);
        Assert.Equal("provider-row", result.GetValue().SizeGateDiagnostics[0].Scope);
        Assert.Single((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public async Task DestinationPolicy_UsesCapturedPlanAndDoesNotResolveAgain()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CoreInMemoryEventStore(domain.EventTypes);
        var publisher = new RecordingDestinationPublisher();
        var executor = CreateExecutor(
            domain,
            store,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "destination",
                ExecutorSizeRepresentation.Destination,
                maxBytesPerEvent: 100,
                measurement: new DelegateMeasurement(context =>
                {
                    Assert.Equal("default", context.ServiceId);
                    Assert.Contains(context.DestinationKey, new[] { "destination-1", "destination-2" });
                    Assert.NotNull(context.DestinationPlan);
                    return ExecutorSizeMeasurementResult.Exact(10);
                }))),
            publisher);

        var result = await executor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = Guid.NewGuid(),
            Location = "Tokyo",
            Date = new DateOnly(2026, 9, 9),
            TemperatureC = 21
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, publisher.CaptureCalls);
        Assert.Equal(1, publisher.PlannedPublishCalls);
        Assert.Equal(0, publisher.LegacyPublishCalls);
        Assert.Equal(["destination-1", "destination-2"], publisher.LastPlanKeys);
    }

    [Fact]
    public async Task DestinationServiceIdentityMismatch_IsStrictFailureOrExplicitNonStrictDiagnostic()
    {
        var domain = DomainType.GetDomainTypes();
        var strictStore = new CoreInMemoryEventStore(domain.EventTypes);
        var strictPublisher = new RecordingDestinationPublisher
        {
            CapturedServiceIdOverride = "publication-service"
        };
        var strictExecutor = CreateExecutor(
            domain,
            strictStore,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "destination",
                ExecutorSizeRepresentation.Destination,
                maxBytesPerEvent: 100,
                measurement: new DelegateMeasurement(_ => ExecutorSizeMeasurementResult.Exact(10)))),
            strictPublisher);

        var strictResult = await strictExecutor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = Guid.NewGuid(),
            Location = "Tokyo",
            Date = new DateOnly(2026, 9, 9),
            TemperatureC = 21
        });

        Assert.IsType<ExecutorSizeCapabilityException>(strictResult.GetException());
        Assert.Empty((await strictStore.ReadAllSerializableEventsAsync()).GetValue());

        var nonStrictStore = new CoreInMemoryEventStore(domain.EventTypes);
        var nonStrictPublisher = new RecordingDestinationPublisher
        {
            CapturedServiceIdOverride = "publication-service"
        };
        var nonStrictExecutor = CreateExecutor(
            domain,
            nonStrictStore,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "destination",
                ExecutorSizeRepresentation.Destination,
                maxBytesPerEvent: 100,
                strictness: ExecutorSizeStrictness.NonStrict,
                measurement: new DelegateMeasurement(_ => ExecutorSizeMeasurementResult.Exact(10)))),
            nonStrictPublisher);

        var nonStrictResult = await nonStrictExecutor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = Guid.NewGuid(),
            Location = "Tokyo",
            Date = new DateOnly(2026, 9, 9),
            TemperatureC = 21
        });

        Assert.True(nonStrictResult.IsSuccess);
        var nonStrictExecution = nonStrictResult.GetValue()!;
        Assert.True(nonStrictExecution.Metadata!.TryGetValue("SizeGateDiagnostics", out var diagnostics));
        var diagnosticList = Assert.IsAssignableFrom<IReadOnlyList<ExecutorSizeDiagnostic>>(diagnostics!);
        Assert.Contains(diagnosticList, diagnostic => diagnostic.Scope == "destination");
        Assert.Single((await nonStrictStore.ReadAllSerializableEventsAsync()).GetValue());
        Assert.Equal(1, nonStrictPublisher.PlannedPublishCalls);
    }

    [Fact]
    public async Task ConditionalAndExpectedPositionRoutes_RejectBeforeTheirProviderWrite()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new GateOnlyStore();
        var executor = CreateExecutor(
            domain,
            store,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                "logical-event",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerEvent: 1)));
        var eventId = Guid.NewGuid();

        var handlerCalls = 0;
        var conditional = await executor.ExecuteAsync(
            new GateCommand(eventId),
            (command, context) =>
            {
                handlerCalls++;
                return HandleGateCommand(command, context);
            },
            new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification("gate-key") });

        Assert.IsType<ExecutorSizeLimitExceededException>(conditional.GetException());
        Assert.Equal(1, handlerCalls);
        Assert.Equal(0, store.ConditionalAppendCalls);

        var expectedId = Guid.NewGuid();
        var expectedTag = $"WeatherForecast:{expectedId}";
        var expected = await executor.CommitSerializableEventsWithExpectedTagPositionsAsync(
            new VersionedExpectedTagPositionSerializedCommitRequest(
                VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion,
                [CreateCandidate(domain, expectedId)],
                [new ConsistencyTagEntry(expectedTag, "")],
                [new TagHeadExpectationEntry("default", expectedTag, TagHeadExpectation.NoEnforcement())]));

        Assert.IsType<ExecutorSizeLimitExceededException>(expected.GetException());
        Assert.Equal(0, store.ExpectedPositionWriteCalls);

        var serializedConditional = await ((ISerializedConditionalSekibanDcbExecutor)executor)
            .CommitSerializableEventConditionallyAsync(
                new SerializedConditionalCommitRequest(
                    SerializedConditionalCommitRequest.CurrentVersion,
                    CreateCandidate(domain, Guid.NewGuid()),
                    "serialized-gate-key"));

        Assert.IsType<ExecutorSizeLimitExceededException>(serializedConditional.GetException());
        Assert.Equal(0, store.ConditionalAppendCalls);
    }

    [Fact]
    public async Task LegacyAndOptInDiResolutionRemainAvailable()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CoreInMemoryEventStore(domain.EventTypes);
        var accessor = new InMemoryObjectAccessor(store, domain);

        var legacyServices = new ServiceCollection()
            .AddSingleton(domain)
            .AddSingleton<IEventStore>(store)
            .AddSingleton<IActorObjectAccessor>(accessor)
            .AddTransient<GeneralSekibanExecutor>();
        using (var legacyProvider = legacyServices.BuildServiceProvider())
        {
            var legacyExecutor = legacyProvider.GetRequiredService<GeneralSekibanExecutor>();
            var legacyResult = await legacyExecutor.ExecuteAsync(new CreateWeatherForecast
            {
                ForecastId = Guid.NewGuid(),
                Location = "Tokyo",
                Date = new DateOnly(2026, 9, 9),
                TemperatureC = 21
            });
            Assert.True(legacyResult.IsSuccess);
        }

        var optInStore = new CoreInMemoryEventStore(domain.EventTypes);
        var optInAccessor = new InMemoryObjectAccessor(optInStore, domain);
        var optInServices = new ServiceCollection()
            .AddSingleton(domain)
            .AddSingleton<IEventStore>(optInStore)
            .AddSingleton<IActorObjectAccessor>(optInAccessor)
            .AddSekibanDcbExecutorSizeGate(options => options.Add(new ExecutorSizePolicy(
                "logical-event",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerEvent: 1)))
            .AddTransient<GeneralSekibanExecutor>();
        using var optInProvider = optInServices.BuildServiceProvider();
        Assert.NotNull(optInProvider.GetRequiredService<ExecutorSizeGateOptions>());
        var optInExecutor = optInProvider.GetRequiredService<GeneralSekibanExecutor>();
        var optInResult = await optInExecutor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = Guid.NewGuid(),
            Location = "Tokyo",
            Date = new DateOnly(2026, 9, 9),
            TemperatureC = 21
        });
        Assert.IsType<ExecutorSizeLimitExceededException>(optInResult.GetException());
    }

    private static GeneralSekibanExecutor CreateExecutor(
        DcbDomainTypes domain,
        IEventStore store,
        ExecutorSizeGateOptions options,
        IEventPublisher? publisher = null,
        IActorObjectAccessor? actorAccessor = null,
        IExecutedUserProvider? executedUserProvider = null) =>
        new(store, actorAccessor ?? new InMemoryObjectAccessor(store, domain), domain, options, publisher, executedUserProvider);

    private static SerializedCommitRequest SingleSerializedRequest(DcbDomainTypes domain, Guid id)
    {
        var candidate = CreateCandidate(domain, id);
        return new SerializedCommitRequest(
            [candidate],
            [new ConsistencyTagEntry($"WeatherForecast:{id}", "")]);
    }

    private static SerializableEventCandidate CreateCandidate(
        DcbDomainTypes domain,
        Guid id,
        string summary = "size gate")
    {
        var payload = new WeatherForecastCreated(id, "Tokyo", new DateOnly(2026, 9, 9), 21, summary);
        return new SerializableEventCandidate(
            JsonSerializer.SerializeToUtf8Bytes(payload, domain.JsonSerializerOptions),
            nameof(WeatherForecastCreated),
            [$"WeatherForecast:{id}"]);
    }

    private static Task<ResultBox<EventOrNone>> HandleGateCommand(GateCommand command, ICommandContext _) =>
        Task.FromResult(EventOrNone.Event(
            new WeatherForecastCreated(command.Id, "Tokyo", new DateOnly(2026, 9, 9), 21, "size gate"),
            new WeatherForecastTag(command.Id)));

    private sealed record GateCommand(Guid Id) : ICommand;

    private sealed class DelegateMeasurement(Func<ExecutorSizeMeasurementContext, ExecutorSizeMeasurementResult> measure)
        : IExecutorSizeMeasurement
    {
        public ExecutorSizeMeasurementResult Measure(ExecutorSizeMeasurementContext context) => measure(context);
    }

    private sealed class FixedExecutedUserProvider(string value) : IExecutedUserProvider
    {
        public string GetExecutedUser() => value;
    }

    private sealed class RecordingDestinationPublisher : IEventPublisher, IExecutorSizeDestinationPublisher
    {
        public string? CapturedServiceIdOverride { get; init; }
        public int CaptureCalls { get; private set; }
        public int PlannedPublishCalls { get; private set; }
        public int LegacyPublishCalls { get; private set; }
        public IReadOnlyList<string> LastPlanKeys { get; private set; } = [];

        public ExecutorSizeDestinationPlan CaptureDestinationPlan(
            Event @event,
            IReadOnlyCollection<ITag> tags,
            string serviceId)
        {
            CaptureCalls++;
            return new ExecutorSizeDestinationPlan(
                CapturedServiceIdOverride ?? serviceId,
                ["destination-1", "destination-2"],
                new object());
        }

        public Task PublishAsync(
            IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
            CancellationToken cancellationToken = default)
        {
            LegacyPublishCalls++;
            return Task.CompletedTask;
        }

        public Task PublishAsync(
            IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
            IReadOnlyDictionary<Guid, ExecutorSizeDestinationPlan> destinationPlans,
            CancellationToken cancellationToken = default)
        {
            PlannedPublishCalls++;
            LastPlanKeys = destinationPlans.Values.Single().DestinationKeys;
            return Task.CompletedTask;
        }
    }

    private sealed class GateOnlyStore :
        IEventStore,
        IConditionalEventStore,
        IExpectedTagPositionEventStore,
        IWriteConditionCapabilityProvider
    {
        public int ConditionalAppendCalls { get; private set; }
        public int ExpectedPositionWriteCalls { get; private set; }
        public WriteConditionCapabilityDescriptor DescribeWriteConditions() =>
            WriteConditionCapabilityDescriptor.Supporting(
                "GateOnly",
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
            throw new InvalidOperationException("The size gate must run before the ordinary provider write.");
        }

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
            Task.FromResult(ResultBox.FromValue(string.Empty));

        public Task<ResultBox<ConditionalAppendReceipt>> AppendIfUniqueAsync(
            ConditionalAppendRequest request,
            CancellationToken cancellationToken = default)
        {
            ConditionalAppendCalls++;
            throw new InvalidOperationException("The size gate must run before conditional append.");
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
            throw new InvalidOperationException("The size gate must run before expected-position write.");
        }
    }
}
