using System.Reflection;
using System.Text;
using Azure.Storage.Queues;
using Dcb.Domain;
using Dcb.Domain.Weather;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.AzureQueue;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public sealed class AzureQueueSizeGateTests
{
    [Fact]
    public void PublicRegistrationApis_HaveTheApprovedAdditiveShape()
    {
        var optionsMethod = typeof(OrleansAzureQueueExecutorSizeGateExtensions).GetMethod(
            nameof(OrleansAzureQueueExecutorSizeGateExtensions.AddOrleansAzureQueueStreamMessagePolicy));
        Assert.NotNull(optionsMethod);
        Assert.Equal(typeof(OrleansAzureQueueExecutorSizeGateExtensions), optionsMethod!.DeclaringType);
        Assert.Equal(typeof(ExecutorSizeGateOptions), optionsMethod!.ReturnType);
        Assert.Equal(
            [
                typeof(ExecutorSizeGateOptions),
                typeof(string),
                typeof(long?),
                typeof(long?),
                typeof(ExecutorSizeStrictness)
            ],
            optionsMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.Equal(
            ["options", "streamProviderName", "maxBytesPerEvent", "maxBytesPerOperation", "strictness"],
            optionsMethod.GetParameters().Select(parameter => parameter.Name ?? string.Empty).ToArray());
        Assert.Null(optionsMethod.GetParameters()[2].DefaultValue);
        Assert.Null(optionsMethod.GetParameters()[3].DefaultValue);
        Assert.Equal(ExecutorSizeStrictness.NonStrict, optionsMethod.GetParameters()[4].DefaultValue);

        var servicesMethod = typeof(OrleansAzureQueueExecutorSizeGateExtensions).GetMethod(
            nameof(OrleansAzureQueueExecutorSizeGateExtensions.AddSekibanDcbOrleansAzureQueueStreamMessageSizeGate));
        Assert.NotNull(servicesMethod);
        Assert.Equal(typeof(OrleansAzureQueueExecutorSizeGateExtensions), servicesMethod!.DeclaringType);
        Assert.Equal(typeof(IServiceCollection), servicesMethod!.ReturnType);
        Assert.Equal(
            [
                typeof(IServiceCollection),
                typeof(string),
                typeof(long?),
                typeof(long?),
                typeof(ExecutorSizeStrictness)
            ],
            servicesMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.Equal(
            ["services", "streamProviderName", "maxBytesPerEvent", "maxBytesPerOperation", "strictness"],
            servicesMethod.GetParameters().Select(parameter => parameter.Name ?? string.Empty).ToArray());
        Assert.Null(servicesMethod.GetParameters()[2].DefaultValue);
        Assert.Null(servicesMethod.GetParameters()[3].DefaultValue);
        Assert.Equal(ExecutorSizeStrictness.NonStrict, servicesMethod.GetParameters()[4].DefaultValue);
    }

    [Fact]
    public void Registration_UsesOneDestinationPolicyAndProviderComposableMarker()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbOrleansAzureQueueStreamMessageSizeGate("EventStreamProvider");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<ExecutorSizeGateOptions>();
        var policy = Assert.Single(options.Policies);
        Assert.Equal(OrleansAzureQueueStreamMessageSizeMeasurement.Scope, policy.Scope);
        Assert.Equal(ExecutorSizeRepresentation.Destination, policy.Representation);
        Assert.Equal(OrleansAzureQueueStreamMessageSizeMeasurement.DefaultMaxBytesPerEvent, policy.MaxBytesPerEvent);
        Assert.Equal(ExecutorSizeStrictness.NonStrict, policy.Strictness);
        Assert.IsType<OrleansAzureQueueStreamMessageSizeMeasurement>(policy.Measurement);
        var marker = services.Single(descriptor =>
            descriptor.ServiceType.FullName == "Sekiban.Dcb.SizeGates.ExecutorSizeGateRegistrationMarker").ImplementationInstance;
        Assert.Equal(
            "provider-composable gate registration",
            marker?.GetType().GetProperty("Mechanism", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(marker));
    }

    [Fact]
    public void RegistrationMechanisms_RejectMixedConfigurationInEitherOrder()
    {
        var coreOptions = new ExecutorSizeGateOptions()
            .AddOrleansAzureQueueStreamMessagePolicy("EventStreamProvider");
        var coreRegistrations = new Action<IServiceCollection>[]
        {
            services => services.AddSekibanDcbExecutorSizeGate(options =>
                options.AddOrleansAzureQueueStreamMessagePolicy("EventStreamProvider")),
            services => services.AddSekibanDcbExecutorSizeGate(coreOptions)
        };

        foreach (var coreRegistration in coreRegistrations)
        {
            var composed = new ServiceCollection();
            coreRegistration(composed);
            using var composedProvider = composed.BuildServiceProvider();
            Assert.Single(composedProvider.GetRequiredService<ExecutorSizeGateOptions>().Policies);

            var coreThenProvider = new ServiceCollection();
            coreRegistration(coreThenProvider);
            Assert.Throws<InvalidOperationException>(() =>
                coreThenProvider.AddSekibanDcbOrleansAzureQueueStreamMessageSizeGate("EventStreamProvider"));

            var providerThenCore = new ServiceCollection();
            providerThenCore.AddSekibanDcbOrleansAzureQueueStreamMessageSizeGate("EventStreamProvider");
            Assert.Throws<InvalidOperationException>(() => coreRegistration(providerThenCore));
        }
    }

    [Fact]
    public void InvalidProviderRegistrationDoesNotLeaveACompatibilityMarker()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddSekibanDcbOrleansAzureQueueStreamMessageSizeGate(
                "EventStreamProvider",
                maxBytesPerEvent: 65_537));
        Assert.Empty(services);

        services.AddSekibanDcbExecutorSizeGate(_ => { });
    }

    [Fact]
    public void CertifiedBound_OneArgumentIsBoundOnlyWithoutMeasuredRepresentation()
    {
        var result = ExecutorSizeMeasurementResult.CertifiedBound(123);

        Assert.True(result.IsAvailable);
        Assert.Null(result.Bytes);
        Assert.Null(result.MeasuredRepresentationBytes);
        Assert.Equal(123, result.CertifiedUpperBound);
        Assert.Equal(123, result.CertifiedUpperBoundBytes);
        Assert.Equal(123, result.ComparableBytes);
    }

    [Fact]
    public void CertifiedBound_TwoArgumentsReportsMeasuredAndCertifiedBytes()
    {
        var result = ExecutorSizeMeasurementResult.CertifiedBound(49_152, 65_536);

        Assert.True(result.IsAvailable);
        Assert.Null(result.Bytes);
        Assert.Equal(49_152, result.MeasuredRepresentationBytes);
        Assert.Equal(65_536, result.CertifiedUpperBoundBytes);
        Assert.Equal(65_536, result.ComparableBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => ExecutorSizeMeasurementResult.CertifiedBound(4, 3));
    }

    [Fact]
    public async Task OperationEvidence_ExactTotalsSaturateWithoutCertifiedEvidence()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new InMemoryEventStore(domain.EventTypes);
        var publisher = new FailingDestinationPublisher(failOnCapture: int.MaxValue);
        var measurement = new DelegateMeasurement(_ =>
            ExecutorSizeMeasurementResult.Exact(long.MaxValue / 2 + 1));
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(store, domain),
            domain,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                OrleansAzureQueueStreamMessageSizeMeasurement.Scope,
                ExecutorSizeRepresentation.Destination,
                maxBytesPerOperation: long.MaxValue - 1,
                strictness: ExecutorSizeStrictness.Strict,
                measurement: measurement)),
            publisher);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var result = await executor.CommitSerializableEventsAsync(
            new SerializedCommitRequest(
                [CreateCandidate(domain, first), CreateCandidate(domain, second)],
                [
                    new ConsistencyTagEntry($"WeatherForecast:{first}", ""),
                    new ConsistencyTagEntry($"WeatherForecast:{second}", "")
                ]));

        var exception = Assert.IsType<ExecutorSizeLimitExceededException>(result.GetException());
        Assert.True(exception.IsOperationLimit);
        Assert.False(exception.IsCertifiedUpperBound);
        Assert.Equal(long.MaxValue, exception.MeasuredBytes);
        Assert.Equal(long.MaxValue, exception.MeasuredRepresentationBytes);
        Assert.Null(exception.CertifiedUpperBoundBytes);
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
        Assert.Equal(0, publisher.PlannedPublishCalls);
        Assert.Equal(0, publisher.LegacyPublishCalls);
    }

    [Fact]
    public async Task OperationEvidence_LegacyCertifiedBoundsPreserveCertifiedOnlyEvidence()
    {
        var exception = await ExecuteOperationLimitAsync(
            long.MaxValue - 1,
            ExecutorSizeMeasurementResult.CertifiedBound(long.MaxValue / 2 + 1),
            ExecutorSizeMeasurementResult.CertifiedBound(long.MaxValue / 2 + 1));

        Assert.True(exception.IsOperationLimit);
        Assert.True(exception.IsCertifiedUpperBound);
        Assert.Equal(long.MaxValue, exception.MeasuredBytes);
        Assert.Null(exception.MeasuredRepresentationBytes);
        Assert.Equal(long.MaxValue, exception.CertifiedUpperBoundBytes);
    }

    [Fact]
    public async Task OperationEvidence_MixedExactAndCertifiedBoundsPreserveEachAvailableTotal()
    {
        var exception = await ExecuteOperationLimitAsync(
            35,
            ExecutorSizeMeasurementResult.Exact(10),
            ExecutorSizeMeasurementResult.CertifiedBound(20, 30));

        Assert.True(exception.IsOperationLimit);
        Assert.True(exception.IsCertifiedUpperBound);
        Assert.Equal(40, exception.MeasuredBytes);
        Assert.Equal(30, exception.MeasuredRepresentationBytes);
        Assert.Equal(40, exception.CertifiedUpperBoundBytes);
    }

    [Fact]
    public void ActualAzureQueueV2Adapter_AlwaysUsesConservativeCertifiedBound()
    {
        using var services = new ServiceCollection()
            .AddSerializer()
            .BuildServiceProvider();
        var serializer = services.GetRequiredService<Serializer>();
        var adapter = new global::Orleans.Providers.Streams.AzureQueue.AzureQueueDataAdapterV2(serializer);
        var serialized = CreateSerializableEvent(payloadLength: 256);
        var streamIdGuid = Guid.NewGuid();
        var streamId = StreamId.Create("AllEvents", streamIdGuid);
        var text = adapter.ToQueueMessage(streamId, new[] { serialized }, null, null);
        var measuredTextBytes = Encoding.UTF8.GetByteCount(text);

        var result = Measure(adapter, serialized, streamId, streamIdGuid);

        Assert.Equal(measuredTextBytes, result.MeasuredRepresentationBytes);
        Assert.Equal(
            4L * ((measuredTextBytes + 2L) / 3L),
            result.CertifiedUpperBoundBytes);
        Assert.InRange(
            (result.CertifiedUpperBoundBytes ?? 0) - (result.MeasuredRepresentationBytes ?? 0),
            0,
            measuredTextBytes);
    }

    [Fact]
    public void ActualAzureQueueV2Adapter_CertifiedBoundaryRejectsRawSizeMutant()
    {
        using var services = new ServiceCollection()
            .AddSerializer()
            .BuildServiceProvider();
        var serializer = services.GetRequiredService<Serializer>();
        var adapter = new global::Orleans.Providers.Streams.AzureQueue.AzureQueueDataAdapterV2(serializer);
        var streamIdGuid = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var streamId = StreamId.Create("AllEvents", streamIdGuid);

        var boundaryEvent = FindEventWithAdapterTextBytes(adapter, streamId, 49_152);
        var overEvent = FindEventWithAdapterTextBytes(adapter, streamId, 49_156);
        var boundary = Measure(adapter, boundaryEvent, streamId, streamIdGuid);
        var over = Measure(adapter, overEvent, streamId, streamIdGuid);

        Assert.Equal(49_152, boundary.MeasuredRepresentationBytes);
        Assert.Equal(65_536, boundary.CertifiedUpperBoundBytes);
        Assert.True(boundary.ComparableBytes <= 65_536, "The equal certified quota must remain admissible.");
        Assert.Equal(49_156, over.MeasuredRepresentationBytes);
        Assert.Equal(65_544, over.CertifiedUpperBoundBytes);
        Assert.True(over.ComparableBytes > 65_536, "The conservative certified bound must reject the over-bound case.");

        // A raw-adapter-size mutant would compare 49,156 with the 65,536 quota and incorrectly admit this event.
        var rawSizeMutant = ExecutorSizeMeasurementResult.Exact(over.MeasuredRepresentationBytes!.Value);
        Assert.True(rawSizeMutant.ComparableBytes <= 65_536);
        Assert.True(over.CertifiedUpperBoundBytes > rawSizeMutant.ComparableBytes);
    }

    [Theory]
    [InlineData(49_152, 65_536, true)]
    [InlineData(49_156, 65_544, false)]
    public async Task ProductionCaptureAndEvaluator_UsesConservativeBoundBeforePersistence(
        int adapterTextBytes,
        long certifiedBytes,
        bool shouldPass)
    {
        var domain = DomainType.GetDomainTypes();
        using var services = BuildAzureQueueServices();
        var adapter = services.GetRequiredKeyedService<IQueueDataAdapter<string, IBatchContainer>>("EventStreamProvider");
        var capture = new OrleansAzureQueueDestinationMeasurementCapture(services, "EventStreamProvider");
        var publisher = new CapturingDestinationPublisher(domain, capture);
        var store = new InMemoryEventStore(domain.EventTypes);
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(store, domain),
            domain,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                OrleansAzureQueueStreamMessageSizeMeasurement.Scope,
                ExecutorSizeRepresentation.Destination,
                maxBytesPerEvent: 65_536,
                strictness: ExecutorSizeStrictness.Strict,
                measurement: new OrleansAzureQueueStreamMessageSizeMeasurement("EventStreamProvider"))),
            publisher);

        var candidate = FindCandidateWithAdapterTextBytes(adapter, domain, adapterTextBytes);
        var result = await executor.CommitSerializableEventsAsync(
            new SerializedCommitRequest(
                [candidate],
                [new ConsistencyTagEntry(candidate.Tags.Single(), "")]));

        Assert.Equal(shouldPass, result.IsSuccess);
        Assert.Equal(adapterTextBytes, publisher.LastMeasuredBytes);
        Assert.Equal(certifiedBytes, publisher.LastCertifiedBytes);
        if (shouldPass)
        {
            Assert.Single(result.GetValue().WrittenEvents);
            Assert.Equal(1, publisher.PlannedPublishCalls);
        }
        else
        {
            var exception = Assert.IsType<ExecutorSizeLimitExceededException>(result.GetException());
            Assert.Equal(65_536, exception.LimitBytes);
            Assert.Equal(certifiedBytes, exception.MeasuredBytes);
            Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
            Assert.Equal(0, publisher.PlannedPublishCalls);
            Assert.Equal(0, publisher.LegacyPublishCalls);
        }
    }

    [Fact]
    public async Task ProductionExecutor_NonStrictSecondCaptureFailure_UsesLegacyForTheWholeOperation()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new InMemoryEventStore(domain.EventTypes);
        var publisher = new FailingDestinationPublisher(failOnCapture: 2);
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(store, domain),
            domain,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                OrleansAzureQueueStreamMessageSizeMeasurement.Scope,
                ExecutorSizeRepresentation.Destination,
                maxBytesPerEvent: 65_536,
                strictness: ExecutorSizeStrictness.NonStrict,
                measurement: new DelegateMeasurement(_ => ExecutorSizeMeasurementResult.Exact(10)))),
            publisher);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var result = await executor.CommitSerializableEventsAsync(
            new SerializedCommitRequest(
                [CreateCandidate(domain, first), CreateCandidate(domain, second)],
                [
                    new ConsistencyTagEntry($"WeatherForecast:{first}", ""),
                    new ConsistencyTagEntry($"WeatherForecast:{second}", "")
                ]));

        Assert.True(result.IsSuccess);
        Assert.Single(result.GetValue().SizeGateDiagnostics);
        Assert.Equal(2, result.GetValue().WrittenEvents.Count);
        Assert.Equal(2, publisher.CaptureCalls);
        Assert.Equal(0, publisher.PlannedPublishCalls);
        Assert.Equal(1, publisher.LegacyPublishCalls);
        Assert.Equal(2, publisher.LastLegacyEventCount);
        Assert.Equal(2, (await store.ReadAllSerializableEventsAsync()).GetValue().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionExecutor_NonStrictSecondCaptureFailureAfterEventExcessRejectsBeforeWrite(
        bool certified)
    {
        var execution = await ExecuteNonStrictPartialFailureAsync(
            maxBytesPerEvent: 10,
            maxBytesPerOperation: null,
            CreatePartialMeasurement(certified, 11));

        await AssertPartialFailureRejectedBeforeWriteAsync(
            execution,
            isOperationLimit: false,
            certified,
            measuredBytes: 11,
            measuredRepresentationBytes: certified ? null : 11,
            certifiedUpperBoundBytes: certified ? 11 : null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionExecutor_NonStrictSecondCaptureFailureAfterOperationExcessRejectsBeforeWrite(
        bool certified)
    {
        var execution = await ExecuteNonStrictPartialFailureAsync(
            maxBytesPerEvent: null,
            maxBytesPerOperation: 10,
            CreatePartialMeasurement(certified, 11));

        await AssertPartialFailureRejectedBeforeWriteAsync(
            execution,
            isOperationLimit: true,
            certified,
            measuredBytes: 11,
            measuredRepresentationBytes: certified ? null : 11,
            certifiedUpperBoundBytes: certified ? 11 : null);
    }

    [Fact]
    public async Task ProductionExecutor_StrictCaptureFailure_RejectsOrdinaryRouteBeforeWrite()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new InMemoryEventStore(domain.EventTypes);
        var publisher = new FailingDestinationPublisher(failOnCapture: 1);
        var executor = CreateDestinationExecutor(domain, store, ExecutorSizeStrictness.Strict, publisher);

        var result = await executor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = Guid.NewGuid(),
            Location = "Tokyo",
            Date = new DateOnly(2026, 9, 12),
            TemperatureC = 21
        });

        Assert.IsType<ExecutorSizeCapabilityException>(result.GetException());
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
        Assert.Equal(0, publisher.PlannedPublishCalls);
        Assert.Equal(0, publisher.LegacyPublishCalls);
    }

    [Fact]
    public async Task ProductionExecutor_StrictCaptureFailure_RejectsSerializedAndConditionalRoutesBeforeWrite()
    {
        var domain = DomainType.GetDomainTypes();
        var serializedStore = new InMemoryEventStore(domain.EventTypes);
        var serializedPublisher = new FailingDestinationPublisher(failOnCapture: 1);
        var serializedExecutor = CreateDestinationExecutor(
            domain, serializedStore, ExecutorSizeStrictness.Strict, serializedPublisher);
        var serializedId = Guid.NewGuid();
        var serialized = await serializedExecutor.CommitSerializableEventsAsync(
            new SerializedCommitRequest(
                [CreateCandidate(domain, serializedId)],
                [new ConsistencyTagEntry($"WeatherForecast:{serializedId}", "")]));

        Assert.IsType<ExecutorSizeCapabilityException>(serialized.GetException());
        Assert.Empty((await serializedStore.ReadAllSerializableEventsAsync()).GetValue());

        var conditionalStore = new InMemoryConditionalEventStore(domain.EventTypes);
        var conditionalPublisher = new FailingDestinationPublisher(failOnCapture: 1);
        var conditionalExecutor = CreateDestinationExecutor(
            domain, conditionalStore, ExecutorSizeStrictness.Strict, conditionalPublisher);
        var conditional = await ((ISerializedConditionalSekibanDcbExecutor)conditionalExecutor)
            .CommitSerializableEventConditionallyAsync(
                new SerializedConditionalCommitRequest(
                    SerializedConditionalCommitRequest.CurrentVersion,
                    CreateCandidate(domain, Guid.NewGuid()),
                    "g76-strict-capture-failure"));

        Assert.IsType<ExecutorSizeCapabilityException>(conditional.GetException());
        Assert.Empty((await conditionalStore.ReadAllSerializableEventsAsync()).GetValue());
        Assert.Equal(0, conditionalPublisher.PlannedPublishCalls);
        Assert.Equal(0, conditionalPublisher.LegacyPublishCalls);
    }

    [Theory]
    [InlineData("ordinary", "resolver")]
    [InlineData("ordinary", "context")]
    [InlineData("ordinary", "encoder")]
    [InlineData("serialized", "resolver")]
    [InlineData("serialized", "context")]
    [InlineData("serialized", "encoder")]
    [InlineData("conditional", "resolver")]
    [InlineData("conditional", "context")]
    [InlineData("conditional", "encoder")]
    public async Task ProductionExecutor_StrictCapabilityFailuresRejectEveryRouteBeforeWrite(
        string route,
        string failureKind)
    {
        var domain = DomainType.GetDomainTypes();
        var publisher = new FailureModeDestinationPublisher(failureKind);
        IEventStore store = route == "conditional"
            ? new InMemoryConditionalEventStore(domain.EventTypes)
            : new InMemoryEventStore(domain.EventTypes);
        var executor = CreateDestinationExecutor(
            domain,
            store,
            ExecutorSizeStrictness.Strict,
            publisher,
            new FailureModeMeasurement(failureKind));

        var result = await ExecuteFailureRouteAsync(route, executor, domain);

        Assert.IsType<ExecutorSizeCapabilityException>(result.Exception);
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
        Assert.Equal(0, publisher.PlannedPublishCalls);
        Assert.Equal(0, publisher.LegacyPublishCalls);
    }

    [Theory]
    [InlineData("ordinary", "resolver")]
    [InlineData("ordinary", "context")]
    [InlineData("ordinary", "encoder")]
    [InlineData("serialized", "resolver")]
    [InlineData("serialized", "context")]
    [InlineData("serialized", "encoder")]
    [InlineData("conditional", "resolver")]
    [InlineData("conditional", "context")]
    [InlineData("conditional", "encoder")]
    public async Task ProductionExecutor_NonStrictCapabilityFailuresUseLegacyForEveryRoute(
        string route,
        string failureKind)
    {
        var domain = DomainType.GetDomainTypes();
        var publisher = new FailureModeDestinationPublisher(failureKind);
        IEventStore store = route == "conditional"
            ? new InMemoryConditionalEventStore(domain.EventTypes)
            : new InMemoryEventStore(domain.EventTypes);
        var executor = CreateDestinationExecutor(
            domain,
            store,
            ExecutorSizeStrictness.NonStrict,
            publisher,
            new FailureModeMeasurement(failureKind));

        var result = await ExecuteFailureRouteAsync(route, executor, domain);

        Assert.True(result.IsSuccess, result.Exception?.ToString());
        Assert.Single(result.Diagnostics);
        Assert.Single((await store.ReadAllSerializableEventsAsync()).GetValue());
        Assert.Equal(0, publisher.PlannedPublishCalls);
        Assert.Equal(1, publisher.LegacyPublishCalls);
    }

    [Fact]
    public void ProductionCapture_RejectsMissingAndUnsupportedKeyedOrUnkeyedAdapters()
    {
        var serialized = CreateSerializableEvent(10);
        var configurations = new[]
        {
            (Services: new ServiceCollection(), Expected: "no keyed or unkeyed"),
            (Services: new ServiceCollection().AddSingleton<IQueueDataAdapter<string, IBatchContainer>>(new NoOpQueueDataAdapter()), Expected: "AzureQueueDataAdapterV2"),
            (Services: new ServiceCollection().AddKeyedSingleton<IQueueDataAdapter<string, IBatchContainer>>(
                "EventStreamProvider", new NoOpQueueDataAdapter()), Expected: "AzureQueueDataAdapterV2")
        };

        foreach (var configuration in configurations)
        {
            using var provider = configuration.Services.BuildServiceProvider();
            var capture = new OrleansAzureQueueDestinationMeasurementCapture(provider, "EventStreamProvider");
            var state = capture.Capture(
                "EventStreamProvider",
                "AllEvents",
                serialized.Id,
                serialized,
                null,
                out var failure);

            Assert.Null(state);
            Assert.NotNull(failure);
            Assert.Contains(configuration.Expected, failure, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProductionCapture_IgnoresNamedNoneAsEncodingProof()
    {
        using var services = BuildAzureQueueServices();
        var adapter = services.GetRequiredKeyedService<IQueueDataAdapter<string, IBatchContainer>>("EventStreamProvider");
        var serialized = CreateSerializableEvent(256);
        var capture = new OrleansAzureQueueDestinationMeasurementCapture(services, "EventStreamProvider");

        var state = capture.Capture(
            "EventStreamProvider",
            "AllEvents",
            serialized.Id,
            serialized,
            null,
            out var failure);

        Assert.Null(failure);
        Assert.NotNull(state);
        var key = $"EventStreamProvider|AllEvents|{serialized.Id:D}";
        var planState = new OrleansDestinationPlanState(
            key,
            "EventStreamProvider",
            "AllEvents",
            serialized.Id,
            state,
            null,
            null);
        var plan = new ExecutorSizeDestinationPlan("service", [key], new[] { planState })
        {
            PreparedEvent = serialized
        };
        var result = new OrleansAzureQueueStreamMessageSizeMeasurement("EventStreamProvider").Measure(
            new ExecutorSizeMeasurementContext(
                OrleansAzureQueueStreamMessageSizeMeasurement.Scope,
                ExecutorSizeRepresentation.Destination,
                CreateEvent(serialized),
                serialized,
                "service",
                key,
                plan));

        var measured = Encoding.UTF8.GetByteCount(adapter.ToQueueMessage(
            StreamId.Create("AllEvents", serialized.Id),
            new[] { serialized },
            null,
            null));
        Assert.Equal(measured, result.MeasuredRepresentationBytes);
        Assert.Equal(4L * ((measured + 2L) / 3L), result.CertifiedUpperBoundBytes);
    }

    [Fact]
    public void Measurement_UsesActualAdapterWithoutResolvingQueueClientOrSending()
    {
        using var services = new ServiceCollection()
            .AddSerializer()
            .BuildServiceProvider();
        Assert.Null(services.GetService<QueueServiceClient>());

        var adapter = new global::Orleans.Providers.Streams.AzureQueue.AzureQueueDataAdapterV2(
            services.GetRequiredService<Serializer>());
        var serialized = CreateSerializableEvent(256);
        var streamIdGuid = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var result = Measure(
            adapter,
            serialized,
            StreamId.Create("AllEvents", streamIdGuid),
            streamIdGuid);

        Assert.True(result.IsAvailable);
        Assert.NotNull(result.MeasuredRepresentationBytes);
        Assert.Null(services.GetService<QueueServiceClient>());
    }

    [Fact]
    public void CapturedPlan_RetainsPreparedEventAndProviderStateForTheSameMeasurement()
    {
        using var services = new ServiceCollection()
            .AddSerializer()
            .BuildServiceProvider();
        var serialized = CreateSerializableEvent(10);
        var state = new AzureQueueDestinationMeasurementState(
            new global::Orleans.Providers.Streams.AzureQueue.AzureQueueDataAdapterV2(
                services.GetRequiredService<Serializer>()),
            StreamId.Create("AllEvents", serialized.Id),
            serialized,
            new Dictionary<string, object> { ["trace"] = "value" });
        var destinationState = new OrleansDestinationPlanState(
            "EventStreamProvider|AllEvents|" + serialized.Id.ToString("D"),
            "EventStreamProvider",
            "AllEvents",
            serialized.Id,
            state,
            state.RequestContext,
            null);
        var plan = new ExecutorSizeDestinationPlan(
            "service",
            [destinationState.DestinationKey],
            new[] { destinationState })
        {
            PreparedEvent = serialized
        };

        Assert.Same(serialized, plan.PreparedEvent);
        Assert.Same(state, Assert.Single((IReadOnlyList<OrleansDestinationPlanState>)plan.ProviderState!).MeasurementState);
        Assert.Same(state.RequestContext, Assert.Single((IReadOnlyList<OrleansDestinationPlanState>)plan.ProviderState!).RequestContext);
    }

    [Fact]
    public void UnsupportedDestination_IsNamedUnavailableAndNeverLooksLikeLogicalBytes()
    {
        var serialized = CreateSerializableEvent(10);
        var planState = new OrleansDestinationPlanState(
            "EventStreamProvider|AllEvents|00000000-0000-0000-0000-000000000001",
            "EventStreamProvider",
            "AllEvents",
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            null,
            null,
            "named provider 'EventStreamProvider' resolved a custom adapter, not AzureQueueDataAdapterV2");
        var plan = new ExecutorSizeDestinationPlan(
            "service",
            [planState.DestinationKey],
            new[] { planState })
        {
            PreparedEvent = serialized
        };
        var result = new OrleansAzureQueueStreamMessageSizeMeasurement("EventStreamProvider").Measure(
            new ExecutorSizeMeasurementContext(
                OrleansAzureQueueStreamMessageSizeMeasurement.Scope,
                ExecutorSizeRepresentation.Destination,
                CreateEvent(serialized),
                serialized,
                "service",
                planState.DestinationKey,
                plan));

        Assert.False(result.IsAvailable);
        Assert.Contains("custom adapter", result.Reason, StringComparison.Ordinal);
        Assert.Null(result.ComparableBytes);
    }

    private static ServiceProvider BuildAzureQueueServices()
    {
        var services = new ServiceCollection()
            .AddSerializer();
        services.Configure<AzureQueueOptions>("EventStreamProvider", options =>
            options.ClientOptions.MessageEncoding = QueueMessageEncoding.None);
        services.AddKeyedSingleton<IQueueDataAdapter<string, IBatchContainer>>(
            "EventStreamProvider",
            (serviceProvider, _) => new global::Orleans.Providers.Streams.AzureQueue.AzureQueueDataAdapterV2(
                serviceProvider.GetRequiredService<Serializer>()));
        return services.BuildServiceProvider();
    }

    private static SerializableEventCandidate FindCandidateWithAdapterTextBytes(
        IQueueDataAdapter<string, IBatchContainer> adapter,
        DcbDomainTypes domain,
        int targetBytes)
    {
        var low = 0;
        var high = 200_000;
        while (NormalizedAdapterTextBytes(adapter, domain, CreateSerializableEvent(domain, high)) < targetBytes)
        {
            high *= 2;
        }

        while (low <= high)
        {
            var candidateLength = low + ((high - low) / 2);
            var candidate = CreateSerializableEvent(domain, candidateLength);
            var actual = NormalizedAdapterTextBytes(adapter, domain, candidate);
            if (actual == targetBytes)
            {
                return new SerializableEventCandidate(
                    candidate.Payload,
                    candidate.EventPayloadName,
                    candidate.Tags);
            }

            if (actual < targetBytes)
                low = candidateLength + 1;
            else
                high = candidateLength - 1;
        }

        throw new Xunit.Sdk.XunitException($"Could not create an adapter message of exactly {targetBytes} bytes.");
    }

    private static int NormalizedAdapterTextBytes(
        IQueueDataAdapter<string, IBatchContainer> adapter,
        DcbDomainTypes domain,
        SerializableEvent serialized)
    {
        var eventResult = serialized.ToEvent(domain.EventTypes);
        Assert.True(eventResult.IsSuccess, eventResult.IsSuccess ? null : eventResult.GetException().ToString());
        var normalized = eventResult.GetValue().ToSerializableEvent(domain.EventTypes);
        return AdapterTextBytes(adapter, StreamId.Create("AllEvents", Guid.Empty), normalized);
    }

    private static SerializableEvent CreateSerializableEvent(DcbDomainTypes domain, int summaryLength)
    {
        var id = Guid.Parse("00000000-0000-0000-0000-000000000010");
        var payload = new WeatherForecastCreated(
            id,
            "Tokyo",
            new DateOnly(2026, 9, 12),
            21,
            new string('A', summaryLength));
        return new SerializableEvent(
            Encoding.UTF8.GetBytes(domain.EventTypes.SerializeEventPayload(payload)),
            "000000000000000000100000000001",
            Guid.Empty,
            new EventMetadata(Guid.Empty.ToString(), "SerializedCommit", "SerializedSekibanExecutor"),
            [$"WeatherForecast:{id:D}"],
            nameof(WeatherForecastCreated));
    }

    private static GeneralSekibanExecutor CreateDestinationExecutor(
        DcbDomainTypes domain,
        IEventStore store,
        ExecutorSizeStrictness strictness,
        IEventPublisher publisher,
        IExecutorSizeMeasurement? measurement = null) =>
        new(
            store,
            new InMemoryObjectAccessor(store, domain),
            domain,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                OrleansAzureQueueStreamMessageSizeMeasurement.Scope,
                ExecutorSizeRepresentation.Destination,
                maxBytesPerEvent: 65_536,
                strictness: strictness,
                measurement: measurement ?? new DelegateMeasurement(_ => ExecutorSizeMeasurementResult.Exact(10)))),
            publisher);

    private static async Task<FailureRouteResult> ExecuteFailureRouteAsync(
        string route,
        GeneralSekibanExecutor executor,
        DcbDomainTypes domain)
    {
        if (route == "ordinary")
        {
            var ordinary = await executor.ExecuteAsync(new CreateWeatherForecast
            {
                ForecastId = Guid.NewGuid(),
                Location = "Tokyo",
                Date = new DateOnly(2026, 9, 12),
                TemperatureC = 21
            });
            return new FailureRouteResult(
                ordinary.IsSuccess,
                ordinary.IsSuccess ? ReadOrdinaryDiagnostics(ordinary.GetValue().Metadata) : [],
                ordinary.IsSuccess ? null : ordinary.GetException());
        }

        var id = Guid.NewGuid();
        if (route == "serialized")
        {
            var serialized = await executor.CommitSerializableEventsAsync(
                new SerializedCommitRequest(
                    [CreateCandidate(domain, id)],
                    [new ConsistencyTagEntry($"WeatherForecast:{id}", "")]));
            return new FailureRouteResult(
                serialized.IsSuccess,
                serialized.IsSuccess ? serialized.GetValue().SizeGateDiagnostics : [],
                serialized.IsSuccess ? null : serialized.GetException());
        }

        if (route == "conditional")
        {
            var conditional = await ((ISerializedConditionalSekibanDcbExecutor)executor)
                .CommitSerializableEventConditionallyAsync(
                    new SerializedConditionalCommitRequest(
                        SerializedConditionalCommitRequest.CurrentVersion,
                        CreateCandidate(domain, id),
                        $"g76-failure-{id:N}"));
            return new FailureRouteResult(
                conditional.IsSuccess,
                conditional.IsSuccess ? conditional.GetValue().SizeGateDiagnostics : [],
                conditional.IsSuccess ? null : conditional.GetException());
        }

        throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown failure-test route.");
    }

    private static IReadOnlyList<ExecutorSizeDiagnostic> ReadOrdinaryDiagnostics(
        IReadOnlyDictionary<string, object>? metadata) =>
        metadata is not null &&
        metadata.TryGetValue("SizeGateDiagnostics", out var value) &&
        value is IReadOnlyList<ExecutorSizeDiagnostic> diagnostics
            ? diagnostics
            : [];

    private static SerializableEventCandidate CreateCandidate(DcbDomainTypes domain, Guid id)
    {
        var payload = new WeatherForecastCreated(
            id,
            "Tokyo",
            new DateOnly(2026, 9, 12),
            21,
            "G76 capture test");
        return new SerializableEventCandidate(
            Encoding.UTF8.GetBytes(domain.EventTypes.SerializeEventPayload(payload)),
            nameof(WeatherForecastCreated),
            [$"WeatherForecast:{id}"]);
    }

    private static async Task<ExecutorSizeLimitExceededException> ExecuteOperationLimitAsync(
        long operationLimit,
        params ExecutorSizeMeasurementResult[] measurementResults)
    {
        var domain = DomainType.GetDomainTypes();
        var store = new InMemoryEventStore(domain.EventTypes);
        var publisher = new FailingDestinationPublisher(failOnCapture: int.MaxValue);
        var remaining = new Queue<ExecutorSizeMeasurementResult>(measurementResults);
        var measurement = new DelegateMeasurement(_ => remaining.Dequeue());
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(store, domain),
            domain,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                OrleansAzureQueueStreamMessageSizeMeasurement.Scope,
                ExecutorSizeRepresentation.Destination,
                maxBytesPerOperation: operationLimit,
                strictness: ExecutorSizeStrictness.Strict,
                measurement: measurement)),
            publisher);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var result = await executor.CommitSerializableEventsAsync(
            new SerializedCommitRequest(
                [CreateCandidate(domain, first), CreateCandidate(domain, second)],
                [
                    new ConsistencyTagEntry($"WeatherForecast:{first}", ""),
                    new ConsistencyTagEntry($"WeatherForecast:{second}", "")
                ]));

        var exception = Assert.IsType<ExecutorSizeLimitExceededException>(result.GetException());
        Assert.Empty(remaining);
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
        Assert.Equal(0, publisher.PlannedPublishCalls);
        Assert.Equal(0, publisher.LegacyPublishCalls);
        return exception;
    }

    private static async Task<NonStrictPartialFailureResult> ExecuteNonStrictPartialFailureAsync(
        long? maxBytesPerEvent,
        long? maxBytesPerOperation,
        ExecutorSizeMeasurementResult firstMeasurement)
    {
        var domain = DomainType.GetDomainTypes();
        var store = new InMemoryEventStore(domain.EventTypes);
        var publisher = new FailingDestinationPublisher(failOnCapture: 2);
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(store, domain),
            domain,
            new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
                OrleansAzureQueueStreamMessageSizeMeasurement.Scope,
                ExecutorSizeRepresentation.Destination,
                maxBytesPerEvent,
                maxBytesPerOperation,
                ExecutorSizeStrictness.NonStrict,
                new DelegateMeasurement(_ => firstMeasurement))),
            publisher);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var result = await executor.CommitSerializableEventsAsync(
            new SerializedCommitRequest(
                [CreateCandidate(domain, first), CreateCandidate(domain, second)],
                [
                    new ConsistencyTagEntry($"WeatherForecast:{first}", ""),
                    new ConsistencyTagEntry($"WeatherForecast:{second}", "")
                ]));

        return new(
            Assert.IsType<ExecutorSizeLimitExceededException>(result.GetException()),
            store,
            publisher);
    }

    private static async Task AssertPartialFailureRejectedBeforeWriteAsync(
        NonStrictPartialFailureResult execution,
        bool isOperationLimit,
        bool certified,
        long measuredBytes,
        long? measuredRepresentationBytes,
        long? certifiedUpperBoundBytes)
    {
        Assert.Equal(isOperationLimit, execution.Exception.IsOperationLimit);
        Assert.Equal(certified, execution.Exception.IsCertifiedUpperBound);
        Assert.Equal(measuredBytes, execution.Exception.MeasuredBytes);
        Assert.Equal(measuredRepresentationBytes, execution.Exception.MeasuredRepresentationBytes);
        Assert.Equal(certifiedUpperBoundBytes, execution.Exception.CertifiedUpperBoundBytes);
        Assert.Empty((await execution.Store.ReadAllSerializableEventsAsync()).GetValue());
        Assert.Equal(2, execution.Publisher.CaptureCalls);
        Assert.Equal(0, execution.Publisher.PlannedPublishCalls);
        Assert.Equal(0, execution.Publisher.LegacyPublishCalls);
    }

    private static ExecutorSizeMeasurementResult CreatePartialMeasurement(bool certified, long bytes) =>
        certified
            ? ExecutorSizeMeasurementResult.CertifiedBound(bytes)
            : ExecutorSizeMeasurementResult.Exact(bytes);

    private sealed record NonStrictPartialFailureResult(
        ExecutorSizeLimitExceededException Exception,
        InMemoryEventStore Store,
        FailingDestinationPublisher Publisher);

    private sealed class CapturingDestinationPublisher(
        DcbDomainTypes domain,
        OrleansAzureQueueDestinationMeasurementCapture capture)
        : IEventPublisher, IExecutorSizeDestinationPublisher
    {
        public int PlannedPublishCalls { get; private set; }
        public int LegacyPublishCalls { get; private set; }
        public long? LastMeasuredBytes { get; private set; }
        public long? LastCertifiedBytes { get; private set; }

        public ExecutorSizeDestinationPlan? CaptureDestinationPlan(
            Event @event,
            IReadOnlyCollection<ITag> tags,
            string serviceId)
        {
            var serialized = @event.ToSerializableEvent(domain.EventTypes);
            var key = "EventStreamProvider|AllEvents|00000000-0000-0000-0000-000000000000";
            var state = capture.Capture(
                "EventStreamProvider",
                "AllEvents",
                Guid.Empty,
                serialized,
                null,
                out var failure);
            var destinationState = new OrleansDestinationPlanState(
                key,
                "EventStreamProvider",
                "AllEvents",
                Guid.Empty,
                state,
                null,
                failure);
            var plan = new ExecutorSizeDestinationPlan(serviceId, [key], new[] { destinationState })
            {
                PreparedEvent = serialized
            };
            var measured = new OrleansAzureQueueStreamMessageSizeMeasurement("EventStreamProvider").Measure(
                new ExecutorSizeMeasurementContext(
                    OrleansAzureQueueStreamMessageSizeMeasurement.Scope,
                    ExecutorSizeRepresentation.Destination,
                    @event,
                    serialized,
                    serviceId,
                    key,
                    plan));
            LastMeasuredBytes = measured.MeasuredRepresentationBytes;
            LastCertifiedBytes = measured.CertifiedUpperBoundBytes;
            return plan;
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
            return Task.CompletedTask;
        }
    }

    private sealed class FailingDestinationPublisher(int failOnCapture) : IEventPublisher, IExecutorSizeDestinationPublisher
    {
        public int CaptureCalls { get; private set; }
        public int PlannedPublishCalls { get; private set; }
        public int LegacyPublishCalls { get; private set; }
        public int LastLegacyEventCount { get; private set; }

        public ExecutorSizeDestinationPlan? CaptureDestinationPlan(
            Event @event,
            IReadOnlyCollection<ITag> tags,
            string serviceId)
        {
            CaptureCalls++;
            return CaptureCalls == failOnCapture
                ? null
                : new ExecutorSizeDestinationPlan(serviceId, ["EventStreamProvider|AllEvents|g76"], new object());
        }

        public Task PublishAsync(
            IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
            CancellationToken cancellationToken = default)
        {
            LegacyPublishCalls++;
            LastLegacyEventCount = events.Count;
            return Task.CompletedTask;
        }

        public Task PublishAsync(
            IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
            IReadOnlyDictionary<Guid, ExecutorSizeDestinationPlan> destinationPlans,
            CancellationToken cancellationToken = default)
        {
            PlannedPublishCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed record FailureRouteResult(
        bool IsSuccess,
        IReadOnlyList<ExecutorSizeDiagnostic> Diagnostics,
        Exception? Exception);

    private sealed class FailureModeDestinationPublisher(string failureKind)
        : IEventPublisher, IExecutorSizeDestinationPublisher
    {
        public int PlannedPublishCalls { get; private set; }
        public int LegacyPublishCalls { get; private set; }

        public ExecutorSizeDestinationPlan? CaptureDestinationPlan(
            Event @event,
            IReadOnlyCollection<ITag> tags,
            string serviceId)
        {
            if (failureKind == "resolver")
                throw new InvalidOperationException("deterministic resolver failure");

            return new ExecutorSizeDestinationPlan(
                serviceId,
                ["EventStreamProvider|AllEvents|g76-failure"],
                failureKind == "context" ? new object() : null);
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
            return Task.CompletedTask;
        }
    }

    private sealed class FailureModeMeasurement(string failureKind) : IExecutorSizeMeasurement
    {
        public ExecutorSizeMeasurementResult Measure(ExecutorSizeMeasurementContext context)
        {
            if (failureKind == "context")
                return ExecutorSizeMeasurementResult.Unavailable("request-context capture failed");

            if (failureKind == "encoder")
                throw new FormatException("deterministic encoder failure");

            return ExecutorSizeMeasurementResult.Exact(10);
        }
    }

    private sealed class DelegateMeasurement(
        Func<ExecutorSizeMeasurementContext, ExecutorSizeMeasurementResult> measure) : IExecutorSizeMeasurement
    {
        public ExecutorSizeMeasurementResult Measure(ExecutorSizeMeasurementContext context) => measure(context);
    }

    private sealed class NoOpQueueDataAdapter : IQueueDataAdapter<string, IBatchContainer>
    {
        public string ToQueueMessage<T>(
            StreamId streamId,
            IEnumerable<T> events,
            StreamSequenceToken? token,
            Dictionary<string, object>? requestContext) => string.Empty;

        public IBatchContainer FromQueueMessage(string queueMessage, long sequenceId) =>
            throw new NotSupportedException();
    }

    private static ExecutorSizeMeasurementResult Measure(
        global::Orleans.Streams.IQueueDataAdapter<string, global::Orleans.Streams.IBatchContainer> adapter,
        SerializableEvent serialized,
        StreamId streamId,
        Guid streamIdGuid)
    {
        var key = $"EventStreamProvider|AllEvents|{streamIdGuid:D}";
        var state = new AzureQueueDestinationMeasurementState(adapter, streamId, serialized, null);
        var destinationState = new OrleansDestinationPlanState(
            key,
            "EventStreamProvider",
            "AllEvents",
            streamIdGuid,
            state,
            null,
            null);
        var plan = new ExecutorSizeDestinationPlan("service", [key], new[] { destinationState })
        {
            PreparedEvent = serialized
        };
        return new OrleansAzureQueueStreamMessageSizeMeasurement("EventStreamProvider").Measure(
            new ExecutorSizeMeasurementContext(
                OrleansAzureQueueStreamMessageSizeMeasurement.Scope,
                ExecutorSizeRepresentation.Destination,
                CreateEvent(serialized),
                serialized,
                "service",
                key,
                plan));
    }

    private static SerializableEvent FindEventWithAdapterTextBytes(
        global::Orleans.Streams.IQueueDataAdapter<string, global::Orleans.Streams.IBatchContainer> adapter,
        StreamId streamId,
        int targetBytes)
    {
        var low = 0;
        var high = 200_000;
        while (AdapterTextBytes(adapter, streamId, CreateSerializableEvent(high)) < targetBytes)
        {
            high *= 2;
        }

        while (low <= high)
        {
            var candidateLength = low + ((high - low) / 2);
            var candidate = CreateSerializableEvent(candidateLength);
            var actual = AdapterTextBytes(adapter, streamId, candidate);
            if (actual == targetBytes)
                return candidate;

            if (actual < targetBytes)
                low = candidateLength + 1;
            else
                high = candidateLength - 1;
        }

        throw new Xunit.Sdk.XunitException($"Could not create an adapter message of exactly {targetBytes} bytes.");
    }

    private static int AdapterTextBytes(
        global::Orleans.Streams.IQueueDataAdapter<string, global::Orleans.Streams.IBatchContainer> adapter,
        StreamId streamId,
        SerializableEvent serialized) =>
        Encoding.UTF8.GetByteCount(adapter.ToQueueMessage(streamId, new[] { serialized }, null, null));

    private static SerializableEvent CreateSerializableEvent(int payloadLength) =>
        new(
            Enumerable.Repeat((byte)0x41, payloadLength).ToArray(),
            "20260912000000000000000000000000-0000000000000001",
            Guid.Parse("00000000-0000-0000-0000-000000000004"),
            new EventMetadata("causation", "correlation", "test"),
            [],
            "TestPayload");

    private static Event CreateEvent(SerializableEvent serialized) =>
        new(
            new TestPayload(serialized.Payload.Length),
            serialized.SortableUniqueIdValue,
            serialized.EventPayloadName,
            serialized.Id,
            serialized.EventMetadata,
            serialized.Tags);

    private sealed record TestPayload(int Length) : IEventPayload;

}
