using Dcb.Domain;
using Dcb.Domain.Student;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.TestSupport;
using System.Text;
using Xunit;
namespace Sekiban.Dcb.WithResult.Tests.Actors;

/// <summary>
///     Issue #1075: a per-event fold that crashes during catch-up was swallowed, and the projection presented as an
///     empty success — "durable events exist but cannot be projected" masked as "there is no data", the most
///     expensive failure to notice. These tests hold the projection to the opposite: a fault surfaces, every read of
///     it fails with context, a retry cannot re-apply the pre-poison events, and only a rebuild clears it. Healthy
///     projections are byte-for-byte unchanged.
///     The poison is the SEK-G13 malformed-payload fixture, reused verbatim.
/// </summary>
public class ProjectionFaultIntegrityTests
{
    private static DcbDomainTypes Domain() =>
        DcbDomainTypesExtensions.Simple(builder =>
        {
            builder.EventTypes.RegisterEventType<StudentCreated>();
            // The fixture event type, so its malformed payload reaches the deserialize boundary.
            builder.EventTypes.RegisterEventType<FixtureStudentCreated>(MalformedEventPayloadFixture.EventTypeName);
            builder.MultiProjectorTypes.RegisterProjector<StudentSummaries>();
        });

    private static SerializableEvent Poison() =>
        new(
            Encoding.UTF8.GetBytes(MalformedEventPayloadFixture.TopLevelPascalCase),
            SortableUniqueId.Generate(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), Guid.Empty),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            new EventMetadata("c", "co", "u"),
            [],
            MalformedEventPayloadFixture.EventTypeName);

    private static Event Healthy() =>
        new(
            new StudentCreated(Guid.NewGuid(), "Taro", 2),
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
            typeof(StudentCreated).FullName!,
            Guid.NewGuid(),
            new EventMetadata("c", "co", "u"),
            []);

    [Fact]
    public async Task PoisonEvent_FaultsTheProjection_AndSurfacesTheOriginalException()
    {
        var actor = new GeneralMultiProjectionActor(Domain(), StudentSummaries.MultiProjectorName);

        // The original binding exception propagates, un-wrapped — not swallowed, not turned into empty success.
        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => actor.AddSerializableEventsAsync([Poison()]));
        Assert.IsNotType<SekibanProjectionFaultException>(thrown); // it is the ORIGINAL fold/deserialize exception

        Assert.True(actor.IsFaulted);
        var fault = actor.CurrentFault!;
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), fault.EventId);
        Assert.Equal(MalformedEventPayloadFixture.EventTypeName, fault.EventType);
        Assert.Equal(StudentSummaries.MultiProjectorName, fault.ProjectorName);

        // the fault's identifiers rode out on the exception's Data, under the SEK-G9 boundary keys
        Assert.Equal(fault.EventId.ToString(), thrown.Data[ProjectionFaultDescriptor.EventIdDataKey]);
        Assert.Equal($"MultiProjection.Fold ({StudentSummaries.MultiProjectorName})", thrown.Data[ProjectionFaultDescriptor.OperationDataKey]);
        Assert.Equal(MalformedEventPayloadFixture.EventTypeName, thrown.Data[ProjectionFaultDescriptor.TargetDataKey]);
        Assert.Equal(fault.Position, thrown.Data[ProjectionFaultDescriptor.PositionDataKey]);
        Assert.False(thrown.Data.Contains(ProjectionFaultDescriptor.ReRaiseDataKey));
        Assert.False(string.IsNullOrEmpty(thrown.StackTrace));
    }

    [Fact]
    public async Task AFaultedProjection_FailsGetState_WithFaultContext()
    {
        var actor = new GeneralMultiProjectionActor(Domain(), StudentSummaries.MultiProjectorName);
        await Assert.ThrowsAnyAsync<Exception>(() => actor.AddSerializableEventsAsync([Poison()]));

        // The read surface every query fetches first: it fails, and the failure names the fault, instead of handing
        // back the last pre-crash payload as a success.
        var state = await actor.GetStateAsync();
        Assert.False(state.IsSuccess);
        var ex = Assert.IsType<SekibanProjectionFaultException>(state.GetException());
        Assert.Equal(MalformedEventPayloadFixture.EventTypeName, ex.Fault.EventType);
        Assert.True(ex.IsReRaise);
        Assert.Equal(true, ex.Data[ProjectionFaultDescriptor.ReRaiseDataKey]);
        Assert.Contains("previously faulted at event", ex.Message, StringComparison.Ordinal);
        Assert.Contains("first observed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FirstFault_LogsTheOriginalOnce_AndReRaisesUseADistinctEventId()
    {
        var logger = new RecordingLogger();
        var actor = new GeneralMultiProjectionActor(Domain(), StudentSummaries.MultiProjectorName, logger: logger);

        var original = await Assert.ThrowsAnyAsync<Exception>(() => actor.AddSerializableEventsAsync([Poison()]));
        _ = await actor.GetStateAsync();
        _ = await actor.GetStateAsync();

        var first = Assert.Single(logger.Entries, entry => entry.EventId.Name == "ProjectionFaultFirstObserved");
        Assert.Equal(1401, first.EventId.Id);
        Assert.Same(original, first.Exception);
        Assert.False(string.IsNullOrEmpty(first.Exception!.StackTrace));

        var reRaises = logger.Entries.Where(entry => entry.EventId.Name == "ProjectionFaultReRaised").ToArray();
        Assert.Equal(2, reRaises.Length);
        Assert.All(reRaises, entry =>
        {
            Assert.Equal(1402, entry.EventId.Id);
            Assert.Null(entry.Exception);
            Assert.Contains("IsReRaise", entry.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ProjectionFaultPublicShape_IsAdditive_AndDescriptorKeepsSixPositionalFields()
    {
        var descriptor = typeof(ProjectionFaultDescriptor);
        var deconstruct = Assert.Single(descriptor.GetMethods(), method => method.Name == "Deconstruct");
        Assert.Equal(6, deconstruct.GetParameters().Length);
        Assert.Equal(6, Assert.Single(descriptor.GetConstructors()).GetParameters().Length);

        var reRaise = typeof(SekibanProjectionFaultException).GetProperty(nameof(SekibanProjectionFaultException.IsReRaise));
        Assert.NotNull(reRaise);
        Assert.True(reRaise!.CanRead);
        Assert.False(reRaise.CanWrite);
    }

    [Fact]
    public async Task AnUnrelatedSuccessfulEvent_DoesNotClearTheFault_AndIsRejected()
    {
        var actor = new GeneralMultiProjectionActor(Domain(), StudentSummaries.MultiProjectorName);
        await Assert.ThrowsAnyAsync<Exception>(() => actor.AddSerializableEventsAsync([Poison()]));

        // A later, perfectly valid event does not "heal" the projection. It is rejected outright — a faulted
        // projection applies nothing — so a catch-up retry cannot re-run the events before the poison and double-count
        // them. And the rejection is the fault, not a fresh deserialize.
        var rejected = await Assert.ThrowsAsync<SekibanProjectionFaultException>(
            () => actor.AddEventsAsync([Healthy()]));
        Assert.Equal(MalformedEventPayloadFixture.EventTypeName, rejected.Fault.EventType);

        Assert.True(actor.IsFaulted);
        Assert.False((await actor.GetStateAsync()).IsSuccess);
    }

    [Fact]
    public async Task RetryingTheSamePoisonBatch_IsRejected_NotReApplied()
    {
        var actor = new GeneralMultiProjectionActor(Domain(), StudentSummaries.MultiProjectorName);

        var first = await Assert.ThrowsAnyAsync<Exception>(() => actor.AddSerializableEventsAsync([Poison()]));
        Assert.IsNotType<SekibanProjectionFaultException>(first); // first time: the real deserialize/fold failure

        var second = await Assert.ThrowsAsync<SekibanProjectionFaultException>(
            () => actor.AddSerializableEventsAsync([Poison()])); // retry: rejected before re-processing anything
        Assert.Equal(actor.CurrentFault!.EventId, second.Fault.EventId);
    }

    [Fact]
    public async Task ClearFaultForRebuild_LetsAFreshReplaySucceed()
    {
        var actor = new GeneralMultiProjectionActor(Domain(), StudentSummaries.MultiProjectorName);
        await Assert.ThrowsAnyAsync<Exception>(() => actor.AddSerializableEventsAsync([Poison()]));
        Assert.True(actor.IsFaulted);

        actor.ClearFaultForRebuild();
        Assert.False(actor.IsFaulted);

        // After the rebuild reset, a healthy event applies and the state reads successfully again.
        await actor.AddEventsAsync([Healthy()]);
        Assert.True((await actor.GetStateAsync()).IsSuccess);
    }

    [Fact]
    public async Task HealthyProjection_IsUnaffected()
    {
        var actor = new GeneralMultiProjectionActor(Domain(), StudentSummaries.MultiProjectorName);

        await actor.AddEventsAsync([Healthy()]);

        Assert.False(actor.IsFaulted);
        Assert.Null(actor.CurrentFault);
        Assert.True((await actor.GetStateAsync()).IsSuccess);
    }

    // --- InMemory accessor: the swallow removal, at the boundary GetActorAsync already turns into ResultBox.Error ---

    [Fact]
    public async Task InMemoryAccessor_PoisonInStore_SurfacesError_NotAnEmptySuccessfulActor()
    {
        // A poison payload that is ALREADY durable — written by an older, pre-G13 producer with the wrong casing and
        // sitting in the store — is what #1075 is about: it survives to read/replay. (InMemoryEventStore's own write
        // path now validates and would reject it, which is stricter than a real database, so the read is stubbed to
        // stand in for that already-durable row.)
        var accessor = new InMemoryObjectAccessor(new PoisonReadEventStore(), Domain());

        var actorResult = await accessor.GetActorAsync<GeneralMultiProjectionActor>(StudentSummaries.MultiProjectorName);

        // Before SEK-G14 the replay swallow made this a successful, empty actor. Now the replay failure reaches
        // GetActorAsync's ResultBox.Error boundary un-swallowed.
        Assert.False(actorResult.IsSuccess);
    }

    [Fact]
    public async Task InMemoryAccessor_FailedRead_SurfacesError()
    {
        var domain = Domain();
        var accessor = new InMemoryObjectAccessor(new FailingReadEventStore(), domain);

        var actorResult = await accessor.GetActorAsync<GeneralMultiProjectionActor>(StudentSummaries.MultiProjectorName);

        Assert.False(actorResult.IsSuccess);
        Assert.Contains("read failed", actorResult.GetException().Message);
    }

    /// <summary>An event store whose read returns an already-durable poison event, to drive the replay path.</summary>
    private sealed class PoisonReadEventStore : StubEventStore
    {
        public override Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue<IEnumerable<SerializableEvent>>([Poison()]));
    }

    /// <summary>An event store whose read fails, to prove a failed read is no longer ignored during replay.</summary>
    private sealed class FailingReadEventStore : StubEventStore
    {
        public override Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.Error<IEnumerable<SerializableEvent>>(new InvalidOperationException("read failed")));
    }

    private abstract class StubEventStore : IEventStore
    {
        public virtual Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue<IEnumerable<SerializableEvent>>([]));

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount) => ReadAllSerializableEventsAsync(since);

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) =>
            throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) =>
            throw new NotSupportedException();
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) =>
            throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => throw new NotSupportedException();
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopDisposable.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(eventId, exception, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(EventId EventId, Exception? Exception, string Message);

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}
