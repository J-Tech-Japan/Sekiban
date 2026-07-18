using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.TestSupport;

/// <summary>
///     A conditional event store that delegates every read/write to an inner <see cref="IEventStore" /> but returns a
///     caller-supplied outcome from <see cref="AppendIfUniqueAsync" />. Lets facade tests drive a specific conditional
///     result — in particular a typed <see cref="ConditionalAppendInDoubtException" /> or
///     <see cref="ConditionalAppendCommittedStateCorruptionException" /> — through the WithResult / WithoutResult
///     executors and the serialized boundary, to prove the typed failure propagates without generic wrapping.
/// </summary>
public sealed class OutcomeForcingConditionalEventStore
    : IEventStore, IConditionalEventStore, IWriteConditionCapabilityProvider
{
    private readonly IEventStore _inner;
    private readonly Func<ConditionalAppendRequest, ResultBox<ConditionalAppendReceipt>> _outcome;
    private int _appendAttempts;
    private int _describeCalls;
    private int _readCalls;
    private int _writeCalls;

    public OutcomeForcingConditionalEventStore(
        IEventStore inner,
        Func<ConditionalAppendRequest, ResultBox<ConditionalAppendReceipt>> outcome)
    {
        _inner = inner;
        _outcome = outcome;
    }

    /// <summary>How many times <see cref="AppendIfUniqueAsync" /> was invoked — a store-side-effect counter for tests.</summary>
    public int AppendAttempts => Volatile.Read(ref _appendAttempts);

    /// <summary>How many times the capability descriptor was resolved — for version-gate zero-side-effect assertions.</summary>
    public int DescribeCalls => Volatile.Read(ref _describeCalls);

    /// <summary>How many read operations were invoked — for version-gate zero-side-effect assertions.</summary>
    public int ReadCalls => Volatile.Read(ref _readCalls);

    /// <summary>How many write operations were invoked — for version-gate zero-side-effect assertions.</summary>
    public int WriteCalls => Volatile.Read(ref _writeCalls);

    public WriteConditionCapabilityDescriptor DescribeWriteConditions()
    {
        Interlocked.Increment(ref _describeCalls);
        return WriteConditionCapabilityDescriptor.Supporting("OutcomeForcing", WriteConditionKind.SingleEventUniqueKey);
    }

    public Task<ResultBox<ConditionalAppendReceipt>> AppendIfUniqueAsync(
        ConditionalAppendRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _appendAttempts);
        return Task.FromResult(_outcome(request));
    }

    // ── IEventStore: delegation, with read/write side-effect counters ────────────────────────────────
    public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) { Interlocked.Increment(ref _readCalls); return _inner.ReadTagsAsync(tag); }
    public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) { Interlocked.Increment(ref _readCalls); return _inner.GetLatestTagAsync(tag); }
    public Task<ResultBox<bool>> TagExistsAsync(ITag tag) { Interlocked.Increment(ref _readCalls); return _inner.TagExistsAsync(tag); }
    public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) { Interlocked.Increment(ref _readCalls); return _inner.GetEventCountAsync(since); }
    public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) { Interlocked.Increment(ref _readCalls); return _inner.GetAllTagsAsync(tagGroup); }
    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) { Interlocked.Increment(ref _readCalls); return _inner.ReadAllSerializableEventsAsync(since); }
    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) { Interlocked.Increment(ref _readCalls); return _inner.ReadAllSerializableEventsAsync(since, maxCount); }
    public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) { Interlocked.Increment(ref _readCalls); return _inner.ReadSerializableEventAsync(eventId); }
    public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) { Interlocked.Increment(ref _readCalls); return _inner.ReadSerializableEventsByTagAsync(tag, since); }
    public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
        IEnumerable<SerializableEvent> events) { Interlocked.Increment(ref _writeCalls); return _inner.WriteSerializableEventsAsync(events); }
    public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() { Interlocked.Increment(ref _readCalls); return _inner.GetLatestSortableUniqueIdAsync(); }
}
