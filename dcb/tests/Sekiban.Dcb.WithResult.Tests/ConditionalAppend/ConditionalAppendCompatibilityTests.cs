using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     Compatibility proof for SEK-G15: the conditional-append feature is strictly additive. Frozen public-API baselines
///     fail if <see cref="IEventStore" />, the core command context, or the positional serialized DTO grow or change; a
///     hand-rolled EXTERNAL event store (a downstream consumer that predates this change) still compiles and works, is
///     never capability-cast, and reports no write-condition capability.
/// </summary>
public class ConditionalAppendCompatibilityTests
{
    // ---- Frozen public-API baselines (a change to these member sets is a breaking change and must fail here) ----

    [Fact]
    public void IEventStore_PublicSurface_IsUnchanged()
    {
        var members = typeof(IEventStore).GetMethods().Select(m => m.Name).OrderBy(n => n).ToArray();
        var expected = new[]
        {
            "GetAllTagsAsync",
            "GetEventCountAsync",
            "GetLatestSortableUniqueIdAsync",
            "GetLatestTagAsync",
            "ReadAllSerializableEventsAsync", // (since) and (since, maxCount) share this name
            "ReadAllSerializableEventsAsync",
            "ReadSerializableEventAsync",
            "ReadSerializableEventsByTagAsync",
            "ReadTagsAsync",
            "TagExistsAsync",
            "WriteSerializableEventsAsync"
        }.OrderBy(n => n).ToArray();

        Assert.Equal(expected, members);
        // The conditional-append member must NOT have leaked onto IEventStore.
        Assert.DoesNotContain("AppendIfUniqueAsync", members);
    }

    [Fact]
    public void SerializedCommitRequest_PositionalShape_IsUnchanged()
    {
        var ctor = typeof(SerializedCommitRequest).GetConstructors().Single();
        var parameters = ctor.GetParameters().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "EventCandidates", "ConsistencyTags" }, parameters);
    }

    [Fact]
    public void CoreCommandContext_AppendEventSurface_IsUnchanged()
    {
        var appendEventOverloads = typeof(Sekiban.Dcb.Commands.ICoreCommandContext)
            .GetMethods()
            .Count(m => m.Name == "AppendEvent");
        Assert.Equal(2, appendEventOverloads); // (payload, params tags) and (EventPayloadWithTags) — no conditional member added
    }

    // ---- External consumer fixture: a downstream IEventStore implemented before SEK-G15 keeps compiling ----

    [Fact]
    public void ExternalConsumerStore_CompilesAndReportsNoWriteCondition()
    {
        var store = new LegacyExternalEventStore();

        // It implements only IEventStore; it is not an IConditionalEventStore, and the runtime probe reports nothing.
        Assert.False(store is IConditionalEventStore);
        var descriptor = SekibanDcbCapabilityResolver.DescribeWriteConditions(store, "event store");
        Assert.False(descriptor.Supports(WriteConditionKind.SingleEventUniqueKey));
    }

    [Fact]
    public async Task ExternalConsumerStore_UnconditionalWrite_IsUntouched()
    {
        var store = new LegacyExternalEventStore();
        var written = await store.WriteSerializableEventsAsync(new[]
        {
            new SerializableEvent(new byte[] { 1 }, SortableUniqueId.GenerateNew(), Guid.CreateVersion7(),
                new EventMetadata("c", "c", "u"), new List<string>(), "Evt")
        });
        Assert.True(written.IsSuccess);
        Assert.Single(store.Written);
    }

    /// <summary>
    ///     A minimal, hand-rolled external event store representing a downstream implementor (e.g. WasmRuntime, or
    ///     SekibanAsAService's ServicePartitionedEventStore) that was written before SEK-G15 and implements ONLY
    ///     <see cref="IEventStore" />. That it compiles against the new assemblies is the compatibility proof.
    /// </summary>
    private sealed class LegacyExternalEventStore : IEventStore
    {
        public List<SerializableEvent> Written { get; } = new();

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
        {
            Written.AddRange(events);
            return Task.FromResult(
                ResultBox.FromValue(
                    ((IReadOnlyList<SerializableEvent>)Written.ToList(),
                        (IReadOnlyList<TagWriteResult>)new List<TagWriteResult>())));
        }

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) =>
            Task.FromResult(ResultBox.FromValue(Enumerable.Empty<TagStream>()));
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => Task.FromResult(ResultBox.FromValue(false));
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue((long)Written.Count));
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) =>
            Task.FromResult(ResultBox.FromValue(Enumerable.Empty<TagInfo>()));
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue(Written.AsEnumerable()));
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) =>
            Task.FromResult(ResultBox.FromValue(Written.AsEnumerable()));
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue(Enumerable.Empty<SerializableEvent>()));
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
            Task.FromResult(ResultBox.FromValue(string.Empty));
    }
}
