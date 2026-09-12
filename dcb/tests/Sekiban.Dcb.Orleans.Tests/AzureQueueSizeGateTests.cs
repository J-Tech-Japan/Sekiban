using System.Reflection;
using System.Text;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.AzureQueue;
using Sekiban.Dcb.SizeGates;
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
    public void CertifiedBound_ReportsBothBytesAndUsesTheConservativeBase64Formula()
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
    public void ActualAzureQueueV2Adapter_ProducesMeasuredTextAndBothEncodingBounds()
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

        var noneResult = Measure(adapter, serialized, streamId, streamIdGuid, Azure.Storage.Queues.QueueMessageEncoding.None);
        var base64Result = Measure(adapter, serialized, streamId, streamIdGuid, Azure.Storage.Queues.QueueMessageEncoding.Base64);

        Assert.Equal(measuredTextBytes, noneResult.MeasuredRepresentationBytes);
        Assert.Equal(measuredTextBytes, noneResult.CertifiedUpperBoundBytes);
        Assert.Equal(
            4L * ((measuredTextBytes + 2L) / 3L),
            base64Result.CertifiedUpperBoundBytes);
        Assert.InRange(
            (base64Result.CertifiedUpperBoundBytes ?? 0) - (base64Result.MeasuredRepresentationBytes ?? 0),
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
        var boundary = Measure(adapter, boundaryEvent, streamId, streamIdGuid, QueueMessageEncoding.Base64);
        var over = Measure(adapter, overEvent, streamId, streamIdGuid, QueueMessageEncoding.Base64);

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
            streamIdGuid,
            QueueMessageEncoding.None);

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
            new Dictionary<string, object> { ["trace"] = "value" },
            QueueMessageEncoding.None);
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

    private static ExecutorSizeMeasurementResult Measure(
        global::Orleans.Streams.IQueueDataAdapter<string, global::Orleans.Streams.IBatchContainer> adapter,
        SerializableEvent serialized,
        StreamId streamId,
        Guid streamIdGuid,
        Azure.Storage.Queues.QueueMessageEncoding encoding)
    {
        var key = $"EventStreamProvider|AllEvents|{streamIdGuid:D}";
        var state = new AzureQueueDestinationMeasurementState(adapter, streamId, serialized, null, encoding);
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
