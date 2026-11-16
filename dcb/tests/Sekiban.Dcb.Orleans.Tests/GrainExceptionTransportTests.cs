extern alias WithoutResultFacade;

using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.TestingHost;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;
using WithoutResultOrleansDcbExecutor = WithoutResultFacade::Sekiban.Dcb.Orleans.OrleansDcbExecutor;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Grain contracts report failures by faulting their task, and the exception crosses the silo boundary through
///     Orleans' own serialization. Every call here goes from the cluster client to a real silo over the network, so each
///     assertion is about the exception the client deserialized: the Sekiban exception types callers branch on keep their
///     type, message and the properties callers read; System exceptions keep their type and message; an exception Orleans
///     has no codec for arrives in the <c>[TypeName] message</c> form the grain contracts always used for it.
/// </summary>
[Collection("grain-exception-transport")]
public class GrainExceptionTransportTests : IAsyncLifetime
{
    private static readonly InMemoryEventStore InnerEventStore = new();
    private static readonly FailingTagReadEventStore SharedEventStore = new(InnerEventStore);
    private TestCluster _cluster = null!;
    private IClusterClient Client => _cluster.Client;

    public async Task InitializeAsync()
    {
        InnerEventStore.Clear();
        SharedEventStore.Failure = null;
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var id = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"TransportCluster-{id}";
        builder.Options.ServiceId = $"TransportService-{id}";
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
        }
    }

    [Fact]
    public async Task ProjectionFault_KeepsTypeMessageAndDescriptor_OnTheStateSurfaces()
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(ProjectionFaultOrleansTests.FaultTestProjector.MultiProjectorName);
        var poison = ProjectionFaultOrleansTests.Event(poison: true, tick: 9_100);
        await grain.SeedEventsAsync([poison]);
        await grain.RefreshAsync();

        var fromState = await Assert.ThrowsAsync<SekibanProjectionFaultException>(() => grain.GetStateAsync());
        var fromSnapshot = await Assert.ThrowsAsync<SekibanProjectionFaultException>(() => grain.GetSnapshotJsonAsync());

        foreach (var fault in new[] { fromState, fromSnapshot })
        {
            Assert.Equal(poison.Id, fault.Fault.EventId);
            Assert.Equal(nameof(ProjectionFaultOrleansTests.FaultTriggerEvent), fault.Fault.EventType);
            Assert.Equal(ProjectionFaultOrleansTests.FaultTestProjector.MultiProjectorName, fault.Fault.ProjectorName);
            Assert.Equal(poison.SortableUniqueIdValue, fault.Fault.Position);
            Assert.Contains("poison event", fault.Fault.Message, StringComparison.Ordinal);
            Assert.Contains(ProjectionFaultOrleansTests.FaultTestProjector.MultiProjectorName, fault.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("[SekibanProjectionFaultException]", fault.Message, StringComparison.Ordinal);
            Assert.Equal(poison.Id.ToString(), fault.Data[ProjectionFaultDescriptor.EventIdDataKey]);
        }
    }

    [Fact]
    public async Task ProjectionFault_ReachesBothFacades_AsTheSameExceptionType()
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(ProjectionFaultOrleansTests.FaultTestProjector.MultiProjectorName);
        var poison = ProjectionFaultOrleansTests.Event(poison: true, tick: 9_200);
        await grain.SeedEventsAsync([poison]);
        await grain.RefreshAsync();
        var domain = ProjectionFaultOrleansTests.CreateDomain();

        var withResult = new OrleansDcbExecutor(Client, SharedEventStore, domain);
        var boxed = await withResult.QueryAsync(new ProjectionFaultOrleansTests.FaultCountQuery());
        Assert.False(boxed.IsSuccess);
        var boxedFault = Assert.IsType<SekibanProjectionFaultException>(boxed.GetException());
        Assert.Equal(poison.Id, boxedFault.Fault.EventId);
        Assert.Equal(poison.SortableUniqueIdValue, boxedFault.Fault.Position);

        var withoutResult = new WithoutResultOrleansDcbExecutor(Client, SharedEventStore, domain);
        var thrown = await Assert.ThrowsAsync<SekibanProjectionFaultException>(
            () => withoutResult.QueryAsync(new ProjectionFaultOrleansTests.FaultCountQuery()));
        Assert.Equal(poison.Id, thrown.Fault.EventId);
        Assert.Equal(poison.SortableUniqueIdValue, thrown.Fault.Position);
        Assert.Equal(boxedFault.Message, thrown.Message);
    }

    [Fact]
    public async Task ResetWithAStaleToken_FailsWithTheGrainsOwnInvalidOperationException()
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(ProjectionFaultOrleansTests.FaultTestProjector.MultiProjectorName);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => grain.ResetProjectionFaultAsync(
            new ResetProjectionFaultRequest(
                ProjectionFaultOrleansTests.FaultTestProjector.MultiProjectorName,
                Guid.NewGuid().ToString(),
                "no-such-position")));

        Assert.StartsWith("Projection fault reset rejected:", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReservationConflict_KeepsTheGrainsMessage_WithoutATypePrefix()
    {
        var grain = Client.GetGrain<ITagConsistentGrain>(TagGrainKey("Probe:reserved"));
        await grain.MakeReservationAsync(null);

        var thrown = await Assert.ThrowsAsync<Exception>(() => grain.MakeReservationAsync(null));

        Assert.Equal(typeof(Exception), thrown.GetType());
        Assert.Equal("Tag Probe:reserved is currently reserved", thrown.Message);
    }

    [Fact]
    public async Task ReservationRefreshFailure_WithACodecLessInnerException_ArrivesInTheTypeMessageForm()
    {
        // The actor wraps the failed authoritative read in an Exception whose inner exception is a type Orleans has no
        // codec for, so it travels the way every failure of this contract always did.
        SharedEventStore.Failure = () => new ProbeStoreFailureException("store offline");
        var grain = Client.GetGrain<ITagConsistentGrain>(TagGrainKey("Failing:refresh"));

        var thrown = await Assert.ThrowsAsync<Exception>(() => grain.MakeReservationAsync("063930412334851845000116174724"));

        Assert.Equal(typeof(Exception), thrown.GetType());
        Assert.Equal(
            "[Exception] Tag Failing:refresh has been modified or could not be refreshed authoritatively",
            thrown.Message);
        Assert.Null(thrown.InnerException);
    }

    [Fact]
    public async Task ReservationRefreshFailure_WithASystemInnerException_KeepsTheInnerException()
    {
        SharedEventStore.Failure = () => new TimeoutException("store timed out");
        var grain = Client.GetGrain<ITagConsistentGrain>(TagGrainKey("Failing:timeout"));

        var thrown = await Assert.ThrowsAsync<Exception>(() => grain.MakeReservationAsync("063930412334851845000116174724"));

        Assert.Equal(typeof(Exception), thrown.GetType());
        Assert.Equal("Tag Failing:timeout has been modified or could not be refreshed authoritatively", thrown.Message);
        var inner = Assert.IsType<TimeoutException>(thrown.InnerException);
        Assert.Equal("store timed out", inner.Message);
    }

    [Fact]
    public async Task LatestPosition_TryRead_ReturnsTheValue_AndTheThrowingReadAgrees()
    {
        var grain = Client.GetGrain<ITagConsistentGrain>(TagGrainKey("Probe:latest"));

        Assert.Equal(string.Empty, await grain.TryGetLatestSortableUniqueIdAsync());
        Assert.Equal(string.Empty, await grain.GetLatestSortableUniqueIdAsync());
    }

    [Fact]
    public async Task ExpectedTagPositionConflict_KeepsEveryPair()
    {
        var thrown = await Assert.ThrowsAsync<ExpectedTagPositionConflictException>(
            () => Probe().FailAsync(ProbeFailure.ExpectedTagPositionConflict));

        Assert.Equal(ExpectedConflict().Message, thrown.Message);
        Assert.Equal(ExpectedConflict().Pairs, thrown.Pairs);
    }

    [Fact]
    public async Task ConditionalAppendInDoubt_KeepsReasonIdentityAndInnerException()
    {
        var thrown = await Assert.ThrowsAsync<ConditionalAppendInDoubtException>(
            () => Probe().FailAsync(ProbeFailure.ConditionalAppendInDoubt));

        var expected = ExpectedInDoubt();
        Assert.Equal(expected.Message, thrown.Message);
        Assert.Equal(expected.ProviderName, thrown.ProviderName);
        Assert.Equal(expected.ServiceId, thrown.ServiceId);
        Assert.Equal(expected.DerivedEventId, thrown.DerivedEventId);
        Assert.Equal(expected.Reason, thrown.Reason);
        Assert.Equal(expected.ReasonCode, thrown.ReasonCode);
        Assert.True(thrown.IsRetryable);
        var inner = Assert.IsType<TimeoutException>(thrown.InnerException);
        Assert.Equal("write timed out", inner.Message);
    }

    [Fact]
    public async Task KeyReuseConflict_KeepsFingerprintsProviderAndInnerException()
    {
        var thrown = await Assert.ThrowsAsync<KeyReuseConflictException>(
            () => Probe().FailAsync(ProbeFailure.KeyReuseConflict));

        var expected = ExpectedKeyReuse();
        Assert.Equal(expected.Message, thrown.Message);
        Assert.Equal(expected.ExistingFingerprint, thrown.ExistingFingerprint);
        Assert.Equal(expected.AttemptedFingerprint, thrown.AttemptedFingerprint);
        Assert.Equal(expected.ProviderName, thrown.ProviderName);
        Assert.Equal(ConditionalAppendStatus.KeyReuseConflict, thrown.Status);
        var inner = Assert.IsType<InvalidOperationException>(thrown.InnerException);
        Assert.Equal("unique violation", inner.Message);
    }

    [Fact]
    public async Task TagHeadExpectationValidation_KeepsItsMessage()
    {
        var thrown = await Assert.ThrowsAsync<TagHeadExpectationValidationException>(
            () => Probe().FailAsync(ProbeFailure.TagHeadExpectationValidation));

        Assert.Equal("Exact requires a non-empty position.", thrown.Message);
    }

    [Fact]
    public async Task SystemException_KeepsTypeAndMessage()
    {
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Probe().FailAsync(ProbeFailure.SystemException));

        Assert.Equal("system failure", thrown.Message);
    }

    [Theory]
    [InlineData(ProbeFailure.CodecLessException, "[ProbeStoreFailureException] codec-less failure")]
    [InlineData(ProbeFailure.CodecLessInnerException, "[InvalidOperationException] outer failure")]
    [InlineData(ProbeFailure.NestedCodecLessData, "[InvalidOperationException] outer failure")]
    [InlineData(ProbeFailure.SelfReferencingData, "[InvalidOperationException] refers to itself")]
    [InlineData(ProbeFailure.ValueTupleSelfReferencingData, "[InvalidOperationException] refers to itself")]
    [InlineData(ProbeFailure.LazyInfiniteData, "[InvalidOperationException] lazy data")]
    [InlineData(ProbeFailure.LargeByteArrayData, "[InvalidOperationException] bytes data")]
    public async Task ExceptionWithoutACodec_ArrivesInTheTypeMessageForm(ProbeFailure failure, string expectedMessage)
    {
        var thrown = await Assert.ThrowsAsync<Exception>(() => Probe().FailAsync(failure));

        Assert.Equal(typeof(Exception), thrown.GetType());
        Assert.Equal(expectedMessage, thrown.Message);
    }

    [Fact]
    public async Task ProjectionFaultWithSelfReferencingData_TravelsThroughItsConverter()
    {
        var thrown = await Assert.ThrowsAsync<SekibanProjectionFaultException>(
            () => Probe().FailAsync(ProbeFailure.ProjectionFaultSelfReferencingData));

        var expected = ExpectedFault();
        Assert.Equal(expected.Fault.EventId, thrown.Fault.EventId);
        Assert.Equal(expected.Fault.ProjectorName, thrown.Fault.ProjectorName);
        Assert.Equal(expected.Fault.Position, thrown.Fault.Position);
        Assert.Equal(expected.Message, thrown.Message);
        Assert.False(thrown.Data.Contains("self"));
    }

    [Fact]
    public async Task AggregateWithCodecLessData_ArrivesInTheTypeMessageForm()
    {
        var thrown = await Assert.ThrowsAsync<Exception>(() => Probe().FailAsync(ProbeFailure.AggregateWithCodecLessData));

        Assert.Equal(typeof(Exception), thrown.GetType());
        Assert.StartsWith("[AggregateException] aggregate failure", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AggregateOfFortySystemExceptions_TravelsUnchanged()
    {
        var thrown = await Assert.ThrowsAsync<AggregateException>(
            () => Probe().FailAsync(ProbeFailure.AggregateOfFortySystemExceptions));

        Assert.Equal(40, thrown.InnerExceptions.Count);
        Assert.All(thrown.InnerExceptions, inner => Assert.IsType<InvalidOperationException>(inner));
        Assert.Equal("inner 39", thrown.InnerExceptions[39].Message);
    }

    [Theory]
    [InlineData(ProbeFailure.CancellationWithCodecLessInnerException)]
    [InlineData(ProbeFailure.CancellationWithCodecLessData)]
    public async Task CancellationWithCodecLessContents_StaysACancellationWithItsMessage(ProbeFailure failure)
    {
        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(() => Probe().FailAsync(failure));

        Assert.Equal(typeof(OperationCanceledException), thrown.GetType());
        Assert.Equal("restore cancelled", thrown.Message);
        Assert.Null(thrown.InnerException);
    }

    [Fact]
    public async Task TransportableFailure_IsLeftAsTheSameInstance()
    {
        using var services = new ServiceCollection().AddSerializer().BuildServiceProvider();
        var failure = new AggregateException(
            "aggregate failure",
            Enumerable.Range(0, 40).Select(i => new InvalidOperationException($"inner {i}")));

        var thrown = await Assert.ThrowsAsync<AggregateException>(
            () => GrainFailureTransport.PrepareAsync(Task.FromException<int>(failure), () => services));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public void SelfReferencingFailure_IsJudgedWithoutSerializing()
    {
        using var services = new ServiceCollection().AddSerializer().BuildServiceProvider();
        var failure = new InvalidOperationException("refers to itself");
        failure.Data["self"] = failure;
        var holder = new InvalidOperationException("holds a cycle", new Exception("inner"));
        holder.Data["items"] = new object[] { "text", holder.InnerException! };
        holder.InnerException!.Data["back"] = holder;

        Assert.False(GrainFailureTransport.CanTravel(failure, services));
        Assert.False(GrainFailureTransport.CanTravel(holder, services));
        Assert.True(GrainFailureTransport.CanTravel(new InvalidOperationException("plain"), services));
    }

    private IExceptionTransportProbeGrain Probe() => Client.GetGrain<IExceptionTransportProbeGrain>("probe");

    private static string TagGrainKey(string tag) =>
        ServiceIdGrainKey.Build(new DefaultServiceIdProvider().GetCurrentServiceId(), tag);

    internal static ExpectedTagPositionConflictException ExpectedConflict() =>
        new([
            new TagHeadExpectedObserved("service-a", "Probe:1", TagHeadExpectation.Exact("063930412334851845000116174724"), "063930412334851845000116174999"),
            new TagHeadExpectedObserved("service-a", "Probe:2", TagHeadExpectation.AssertEmpty(), null),
            new TagHeadExpectedObserved("service-a", "Probe:3", TagHeadExpectation.NoEnforcement(), "063930412334851845000116175000")
        ]);

    internal static SekibanProjectionFaultException ExpectedFault() =>
        new(new ProjectionFaultDescriptor(
            Guid.Parse("5b7f0c1e-2d3a-4e5f-8a9b-0c1d2e3f4a5b"),
            "ProbeEvent",
            "probe-projector",
            "063930412334851845000116174724",
            "probe fold failure",
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).Ticks));

    internal static ConditionalAppendInDoubtException ExpectedInDoubt() =>
        ConditionalAppendInDoubtException.Create(
            "probe-provider",
            "service-a",
            Guid.Parse("0f4b8d2a-1c3e-4f5a-9b7c-2d6e8f0a1b3c"),
            ConditionalAppendInDoubtReason.AmbiguousAfterWrite,
            new TimeoutException("write timed out"));

    internal static KeyReuseConflictException ExpectedKeyReuse() =>
        new("existing-fingerprint", "attempted-fingerprint", "probe-provider", new InvalidOperationException("unique violation"));

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton(_ => ProjectionFaultOrleansTests.CreateDomain());
                    services.AddSingleton<IEventStore>(SharedEventStore);
                    services.AddSingleton<IMultiProjectionStateStore, InMemoryMultiProjectionStateStore>();
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    services.AddSingleton<Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 1 });
                    services.AddSekibanDcbNativeRuntime();
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("OrleansStorage")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    /// <summary>An in-memory store whose tag-head reads for the <c>Failing</c> tag group fail with a chosen exception.</summary>
    private sealed class FailingTagReadEventStore(IEventStore inner) : IEventStore
    {
        public Func<Exception>? Failure { get; set; }

        public Task<TagState> GetLatestTagAsync(ITag tag) =>
            Failure is { } failure && tag.GetTagGroup() == "Failing"
                ? Task.FromException<TagState>(failure())
                : inner.GetLatestTagAsync(tag);

        public Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            inner.ReadAllSerializableEventsAsync(since);
        public Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) =>
            inner.ReadAllSerializableEventsAsync(since, maxCount);
        public Task<IEnumerable<TagStream>> ReadTagsAsync(ITag tag) => inner.ReadTagsAsync(tag);
        public Task<bool> TagExistsAsync(ITag tag) => inner.TagExistsAsync(tag);
        public Task<long> GetEventCountAsync(SortableUniqueId? since = null) => inner.GetEventCountAsync(since);
        public Task<IEnumerable<TagInfo>> GetAllTagsAsync(string? tagGroup = null) => inner.GetAllTagsAsync(tagGroup);
        public Task<SerializableEvent?> ReadSerializableEventAsync(Guid eventId) => inner.ReadSerializableEventAsync(eventId);
        public Task<IEnumerable<SerializableEvent>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
            inner.ReadSerializableEventsByTagAsync(tag, since);
        public Task<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)> WriteSerializableEventsAsync(
            IEnumerable<SerializableEvent> events) => inner.WriteSerializableEventsAsync(events);
        public Task<string> GetLatestSortableUniqueIdAsync() => inner.GetLatestSortableUniqueIdAsync();
    }
}

/// <summary>The failure a probe grain call raises.</summary>
public enum ProbeFailure
{
    ExpectedTagPositionConflict,
    ConditionalAppendInDoubt,
    KeyReuseConflict,
    TagHeadExpectationValidation,
    SystemException,
    CodecLessException,
    CodecLessInnerException,
    NestedCodecLessData,
    SelfReferencingData,
    AggregateWithCodecLessData,
    AggregateOfFortySystemExceptions,
    CancellationWithCodecLessInnerException,
    CancellationWithCodecLessData,
    ValueTupleSelfReferencingData,
    ProjectionFaultSelfReferencingData,
    LazyInfiniteData,
    LargeByteArrayData
}

/// <summary>A test grain that fails with a chosen exception through the same transport preparation the Sekiban grains use.</summary>
public interface IExceptionTransportProbeGrain : IGrainWithStringKey
{
    Task<int> FailAsync(ProbeFailure failure);
}

public sealed class ExceptionTransportProbeGrain : Grain, IExceptionTransportProbeGrain
{
    public Task<int> FailAsync(ProbeFailure failure) =>
        GrainFailureTransport.PrepareAsync(Task.FromException<int>(Create(failure)), () => ServiceProvider);

    private static Exception WithData(Exception exception, string key, object value)
    {
        exception.Data[key] = value;
        return exception;
    }

    private static Exception ValueTupleSelfReferencing()
    {
        var exception = new InvalidOperationException("refers to itself");
        exception.Data["self"] = ValueTuple.Create<Exception>(exception);
        return exception;
    }

    private static Exception ProjectionFaultSelfReferencing()
    {
        var fault = GrainExceptionTransportTests.ExpectedFault();
        fault.Data["self"] = fault;
        return fault;
    }

    // Enumerating this never ends; the transport must not touch it.
    private static IEnumerable<int> Endless()
    {
        for (var i = 0; ; i++)
        {
            yield return i;
        }
    }

    private static Exception SelfReferencing()
    {
        var exception = new InvalidOperationException("refers to itself");
        exception.Data["self"] = exception;
        return exception;
    }

    private static Exception Create(ProbeFailure failure) => failure switch
    {
        ProbeFailure.ExpectedTagPositionConflict => GrainExceptionTransportTests.ExpectedConflict(),
        ProbeFailure.ConditionalAppendInDoubt => GrainExceptionTransportTests.ExpectedInDoubt(),
        ProbeFailure.KeyReuseConflict => GrainExceptionTransportTests.ExpectedKeyReuse(),
        ProbeFailure.TagHeadExpectationValidation => new TagHeadExpectationValidationException("Exact requires a non-empty position."),
        ProbeFailure.SystemException => new InvalidOperationException("system failure"),
        ProbeFailure.CodecLessException => new ProbeStoreFailureException("codec-less failure"),
        ProbeFailure.CodecLessInnerException => new InvalidOperationException(
            "outer failure",
            new ProbeStoreFailureException("codec-less inner failure")),
        ProbeFailure.NestedCodecLessData => WithData(
            new InvalidOperationException("outer failure"),
            "cause",
            new InvalidOperationException("cause", new ProbeStoreFailureException("codec-less inner failure"))),
        ProbeFailure.SelfReferencingData => SelfReferencing(),
        ProbeFailure.AggregateWithCodecLessData => WithData(
            new AggregateException("aggregate failure", new InvalidOperationException("inner")),
            "detail",
            new ProbeStoreFailureException("codec-less data")),
        ProbeFailure.AggregateOfFortySystemExceptions => new AggregateException(
            "aggregate failure",
            Enumerable.Range(0, 40).Select(i => new InvalidOperationException($"inner {i}"))),
        ProbeFailure.CancellationWithCodecLessInnerException => new OperationCanceledException(
            "restore cancelled",
            new ProbeStoreFailureException("codec-less inner failure")),
        ProbeFailure.CancellationWithCodecLessData => WithData(
            new OperationCanceledException("restore cancelled"),
            "detail",
            new ProbeStoreFailureException("codec-less data")),
        ProbeFailure.ValueTupleSelfReferencingData => ValueTupleSelfReferencing(),
        ProbeFailure.ProjectionFaultSelfReferencingData => ProjectionFaultSelfReferencing(),
        ProbeFailure.LazyInfiniteData => WithData(new InvalidOperationException("lazy data"), "items", Endless()),
        ProbeFailure.LargeByteArrayData => WithData(new InvalidOperationException("bytes data"), "bytes", new byte[4 * 1024 * 1024]),
        _ => throw new ArgumentOutOfRangeException(nameof(failure))
    };
}

/// <summary>An exception type Orleans has no codec for.</summary>
public sealed class ProbeStoreFailureException(string message) : Exception(message);

[CollectionDefinition("grain-exception-transport", DisableParallelization = true)]
public sealed class GrainExceptionTransportCollection { }
