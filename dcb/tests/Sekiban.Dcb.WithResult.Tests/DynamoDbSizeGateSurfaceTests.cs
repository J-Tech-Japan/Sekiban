using System.Reflection;
using Amazon.DynamoDBv2;
using Dcb.Domain;
using Dcb.Domain.Student;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests;

/// <summary>
/// Focused G67 public-surface and DI propagation inventory. Existing DynamoDB signatures are pinned alongside the
/// additive measurement/gate API so an evidence-only provider capability cannot silently alter compatibility.
/// </summary>
public sealed class DynamoDbSizeGateSurfaceTests
{
    [Fact]
    public void PublicDynamoDbSizeGateAndExistingProviderSurfacesRemainFrozen()
    {
        AssertPublicConstructor(
            typeof(DynamoDbEventItemSizeMeasurement),
            typeof(DynamoDbEventStoreOptions));
        AssertPublicConstructor(
            typeof(DynamoDbMaxWrittenItemSizeMeasurement),
            typeof(DynamoDbEventStoreOptions));
        AssertPublicConstructor(
            typeof(DynamoDbWriteOperationSizeMeasurement),
            typeof(DynamoDbEventStoreOptions));
        AssertPublicMethod(
            typeof(DynamoDbEventItemSizeMeasurement),
            nameof(DynamoDbEventItemSizeMeasurement.Measure),
            typeof(ExecutorSizeMeasurementResult),
            typeof(ExecutorSizeMeasurementContext));
        AssertPublicMethod(
            typeof(DynamoDbMaxWrittenItemSizeMeasurement),
            nameof(DynamoDbMaxWrittenItemSizeMeasurement.Measure),
            typeof(ExecutorSizeMeasurementResult),
            typeof(ExecutorSizeMeasurementContext));
        AssertPublicMethod(
            typeof(DynamoDbWriteOperationSizeMeasurement),
            nameof(DynamoDbWriteOperationSizeMeasurement.Measure),
            typeof(ExecutorSizeMeasurementResult),
            typeof(ExecutorSizeMeasurementContext));

        var maximumBytes = typeof(DynamoDbEventItemSizeMeasurement).GetField(
            nameof(DynamoDbEventItemSizeMeasurement.MaximumItemBytes),
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotNull(maximumBytes);
        Assert.True(maximumBytes!.IsLiteral);
        Assert.Equal(DynamoDbEventItemSizeMeasurement.MaximumItemBytes, maximumBytes.GetRawConstantValue());

        AssertPublicMethod(
            typeof(DynamoDbExecutorSizeGateExtensions),
            nameof(DynamoDbExecutorSizeGateExtensions.AddDynamoDbEventItemPolicy),
            typeof(ExecutorSizeGateOptions),
            typeof(ExecutorSizeGateOptions),
            typeof(DynamoDbEventStoreOptions),
            typeof(Nullable<long>),
            typeof(Nullable<long>),
            typeof(ExecutorSizeStrictness));
        AssertPublicMethod(
            typeof(DynamoDbExecutorSizeGateExtensions),
            nameof(DynamoDbExecutorSizeGateExtensions.AddSekibanDcbDynamoDbEventItemSizeGate),
            typeof(IServiceCollection),
            typeof(IServiceCollection),
            typeof(Nullable<long>),
            typeof(Nullable<long>),
            typeof(ExecutorSizeStrictness));
        AssertPublicMethod(
            typeof(DynamoDbExecutorSizeGateExtensions),
            nameof(DynamoDbExecutorSizeGateExtensions.AddDynamoDbMaxWrittenItemPolicy),
            typeof(ExecutorSizeGateOptions),
            typeof(ExecutorSizeGateOptions),
            typeof(DynamoDbEventStoreOptions),
            typeof(Nullable<long>),
            typeof(ExecutorSizeStrictness));
        AssertPublicMethod(
            typeof(DynamoDbExecutorSizeGateExtensions),
            nameof(DynamoDbExecutorSizeGateExtensions.AddDynamoDbWriteOperationPolicy),
            typeof(ExecutorSizeGateOptions),
            typeof(ExecutorSizeGateOptions),
            typeof(DynamoDbEventStoreOptions),
            typeof(Nullable<long>),
            typeof(ExecutorSizeStrictness));
        AssertPublicMethod(
            typeof(DynamoDbExecutorSizeGateExtensions),
            nameof(DynamoDbExecutorSizeGateExtensions.AddSekibanDcbDynamoDbMaxWrittenItemSizeGate),
            typeof(IServiceCollection),
            typeof(IServiceCollection),
            typeof(Nullable<long>),
            typeof(ExecutorSizeStrictness));
        AssertPublicMethod(
            typeof(DynamoDbExecutorSizeGateExtensions),
            nameof(DynamoDbExecutorSizeGateExtensions.AddSekibanDcbDynamoDbWriteOperationSizeGate),
            typeof(IServiceCollection),
            typeof(IServiceCollection),
            typeof(Nullable<long>),
            typeof(ExecutorSizeStrictness));

        AssertPublicConstructor(
            typeof(DynamoDbEventStore),
            typeof(DynamoDbContext),
            typeof(IEventTypes),
            typeof(IServiceIdProvider),
            typeof(ILogger<DynamoDbEventStore>));
        AssertPublicMethod(
            typeof(DynamoDbEventStore),
            nameof(DynamoDbEventStore.AppendIfUniqueAsync),
            typeof(Task<ResultBox<ConditionalAppendReceipt>>),
            typeof(ConditionalAppendRequest),
            typeof(CancellationToken));
        AssertPublicMethod(
            typeof(DynamoDbEventStore),
            nameof(DynamoDbEventStore.WriteEventsAsync),
            typeof(Task<ResultBox<(IReadOnlyList<Event>, IReadOnlyList<TagWriteResult>)>>),
            typeof(IEnumerable<Event>));
        AssertPublicMethod(
            typeof(DynamoDbEventStore),
            nameof(DynamoDbEventStore.WriteSerializableEventsAsync),
            typeof(Task<ResultBox<(IReadOnlyList<SerializableEvent>, IReadOnlyList<TagWriteResult>)>>),
            typeof(IEnumerable<SerializableEvent>));
        AssertPublicMethod(
            typeof(DynamoDbEventStore),
            nameof(DynamoDbEventStore.StreamSerializableEventsByTagAsync),
            typeof(Task<ResultBox<SerializableEventStreamReadResult>>),
            typeof(ITag),
            typeof(SortableUniqueId),
            typeof(SortableUniqueId),
            typeof(Func<SerializableEvent, ValueTask>),
            typeof(CancellationToken));

        AssertPublicConstructor(
            typeof(DynamoDbEventStoreFactory),
            typeof(DynamoDbContext),
            typeof(IEventTypes),
            typeof(ILoggerFactory));
        AssertPublicMethod(
            typeof(DynamoDbEventStoreFactory),
            nameof(DynamoDbEventStoreFactory.CreateForService),
            typeof(IEventStore),
            typeof(string));

        AssertPublicMethod(
            typeof(SekibanDcbDynamoDbExtensions),
            nameof(SekibanDcbDynamoDbExtensions.AddSekibanDcbDynamoDb),
            typeof(IServiceCollection),
            typeof(IServiceCollection),
            typeof(IConfiguration));
        AssertPublicMethod(
            typeof(SekibanDcbDynamoDbExtensions),
            nameof(SekibanDcbDynamoDbExtensions.AddSekibanDcbDynamoDb),
            typeof(IServiceCollection),
            typeof(IServiceCollection),
            typeof(IAmazonDynamoDB),
            typeof(Action<DynamoDbEventStoreOptions>));
        AssertPublicMethod(
            typeof(SekibanDcbDynamoDbExtensions),
            nameof(SekibanDcbDynamoDbExtensions.AddSekibanDcbDynamoDbWithAspire),
            typeof(IServiceCollection),
            typeof(IServiceCollection));
    }

    [Fact]
    public void DiDynamoOptionsFlowIntoTheGateMeasurement_AndWriteShardsChangeMeasuredBytes()
    {
        var client = DispatchProxy.Create<IAmazonDynamoDB, DynamoDbEventItemSizeMeasurementTests.CapturingDynamoDb>();
        var services = new ServiceCollection()
            .AddSekibanDcbDynamoDb(client, options => options.WriteShardCount = 2)
            .AddSekibanDcbDynamoDbEventItemSizeGate();

        using var provider = services.BuildServiceProvider();
        var gate = provider.GetRequiredService<ExecutorSizeGateOptions>();
        var policy = Assert.Single(gate.Policies);
        var diMeasurement = Assert.IsType<DynamoDbEventItemSizeMeasurement>(policy.Measurement);
        var context = CreateContext();
        var unsharded = new DynamoDbEventItemSizeMeasurement(
            new DynamoDbEventStoreOptions { WriteShardCount = 1 }).Measure(context);
        var sharded = diMeasurement.Measure(context);

        Assert.True(unsharded.IsAvailable, unsharded.Reason);
        Assert.True(sharded.IsAvailable, sharded.Reason);
        Assert.Equal(unsharded.Bytes!.Value + 2, sharded.Bytes!.Value);
    }

    private static ExecutorSizeMeasurementContext CreateContext()
    {
        var serialized = new SerializableEvent(
            System.Text.Encoding.UTF8.GetBytes("{}"),
            SortableUniqueId.GenerateNew(),
            Guid.CreateVersion7(),
            new EventMetadata("cause", "correlation", "user"),
            [],
            nameof(StudentCreated));
        return new ExecutorSizeMeasurementContext(
            DynamoDbEventItemSizeMeasurement.Scope,
            ExecutorSizeRepresentation.StorageItem,
            new Event(
                new StudentCreated(Guid.CreateVersion7(), "surface", 1),
                serialized.SortableUniqueIdValue,
                serialized.EventPayloadName,
                serialized.Id,
                serialized.EventMetadata,
                serialized.Tags),
            serialized,
            "g67-surface",
            null,
            null);
    }

    private static void AssertPublicConstructor(Type type, params Type[] parameterTypes)
    {
        var matches = type
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(parameterTypes))
            .ToArray();
        Assert.Single(matches);
    }

    private static void AssertPublicMethod(
        Type type,
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        var matches = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == name && method.ReturnType == returnType)
            .Where(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(parameterTypes))
            .ToArray();
        Assert.Single(matches);
    }
}
