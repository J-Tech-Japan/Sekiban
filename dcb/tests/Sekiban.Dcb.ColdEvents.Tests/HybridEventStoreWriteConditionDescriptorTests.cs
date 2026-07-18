using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.ColdEvents.Tests;

/// <summary>
///     Write-conditions propagate like durability, but with a stricter rule: the Hybrid advertises (and forwards) a kind
///     only when its HOT store BOTH declares the capability AND actually implements <see cref="IConditionalEventStore" />.
///     A store that only DECLARES the capability (but cannot be forwarded to) must not be honoured — otherwise the
///     executor would pass a descriptor-only preflight and then fail on the forward.
/// </summary>
public class HybridEventStoreWriteConditionDescriptorTests
{
    private static HybridEventStore Wrap(IEventStore hot) =>
        new(
            hot,
            new InMemoryColdObjectStorage(),
            new JsonlColdSegmentFormatHandler(),
            new DefaultServiceIdProvider(),
            Options.Create(new ColdEventStoreOptions { Enabled = true }),
            NullLogger<HybridEventStore>.Instance);

    [Fact]
    public void WrappingAHotStoreThatDeclaresAndImplements_PropagatesTheKind()
    {
        var descriptor = Wrap(new ConditionalStore()).DescribeWriteConditions();

        Assert.True(descriptor.Supports(WriteConditionKind.SingleEventUniqueKey));
        Assert.Equal("HybridEventStore(ConditionalStore)", descriptor.ProviderName);
    }

    [Fact]
    public void WrappingADeceptiveHotStore_ThatDeclaresButDoesNotImplement_AdvertisesNothing()
    {
        // The regression: declaring the capability without implementing IConditionalEventStore must NOT be advertised.
        var descriptor = Wrap(new DeceptiveStore()).DescribeWriteConditions();

        Assert.False(descriptor.Supports(WriteConditionKind.SingleEventUniqueKey));
        Assert.Empty(descriptor.SupportedKinds);
    }

    [Fact]
    public void WrappingAStoreThatCannotCondition_StaysSilent()
    {
        var descriptor = Wrap(new SilentStore()).DescribeWriteConditions();

        Assert.False(descriptor.Supports(WriteConditionKind.SingleEventUniqueKey));
        Assert.Empty(descriptor.SupportedKinds);
    }

    [Fact]
    public async Task Hybrid_ForwardsConditionalAppend_ToTheHotStore()
    {
        var hot = new ConditionalStore();
        var hybrid = Wrap(hot);
        var request = new ConditionalAppendRequest(
            "k",
            new SerializableEvent(new byte[] { 1 }, SortableUniqueId.GenerateNew(), Guid.CreateVersion7(),
                new EventMetadata("c", "c", "u"), new List<string>(), "Evt"));

        var result = await hybrid.AppendIfUniqueAsync(request);

        Assert.True(result.IsSuccess);
        Assert.True(hot.ForwardReceived); // the append was forwarded to the hot store, not handled by the wrapper
    }

    /// <summary>A hot store that declares AND implements the capability (the honest case).</summary>
    private sealed class ConditionalStore : SilentStore, IWriteConditionCapabilityProvider, IConditionalEventStore
    {
        public bool ForwardReceived { get; private set; }

        public WriteConditionCapabilityDescriptor DescribeWriteConditions() =>
            WriteConditionCapabilityDescriptor.Supporting("ConditionalStore", WriteConditionKind.SingleEventUniqueKey);

        public Task<ResultBox<ConditionalAppendReceipt>> AppendIfUniqueAsync(
            ConditionalAppendRequest request,
            CancellationToken cancellationToken = default)
        {
            ForwardReceived = true;
            return Task.FromResult(
                ResultBox.FromValue(
                    new ConditionalAppendReceipt(
                        ConditionalAppendStatus.Appended,
                        request.Event.Id,
                        request.Event.SortableUniqueIdValue,
                        "fingerprint")));
        }
    }

    /// <summary>A hot store that DECLARES the capability but does NOT implement IConditionalEventStore — must not be honoured.</summary>
    private sealed class DeceptiveStore : SilentStore, IWriteConditionCapabilityProvider
    {
        public WriteConditionCapabilityDescriptor DescribeWriteConditions() =>
            WriteConditionCapabilityDescriptor.Supporting("DeceptiveStore", WriteConditionKind.SingleEventUniqueKey);
    }

    /// <summary>An event store that does not implement the capability descriptor at all.</summary>
    private class SilentStore : IEventStore
    {
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) =>
            throw new NotSupportedException();
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
            throw new NotSupportedException();
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => throw new NotSupportedException();
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();
    }
}
