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
///     Write-conditions propagate exactly like durability: a Hybrid reports what its HOT store can enforce (writes land
///     there), never upgrading on its own authority, and stays silent when the hot store is silent.
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
    public void WrappingAConditionalHotStore_PropagatesTheKind()
    {
        var descriptor = Wrap(new ConditionalStore()).DescribeWriteConditions();

        Assert.True(descriptor.Supports(WriteConditionKind.SingleEventUniqueKey));
        Assert.Equal("HybridEventStore(ConditionalStore)", descriptor.ProviderName);
    }

    [Fact]
    public void WrappingAStoreThatCannotCondition_StaysSilent()
    {
        // A durable-but-unconditional hot store: the Hybrid must not invent a write-condition it cannot enforce.
        var descriptor = Wrap(new SilentStore()).DescribeWriteConditions();

        Assert.False(descriptor.Supports(WriteConditionKind.SingleEventUniqueKey));
        Assert.Empty(descriptor.SupportedKinds);
    }

    /// <summary>A hot store that declares the single-event unique-key capability.</summary>
    private sealed class ConditionalStore : SilentStore, IWriteConditionCapabilityProvider
    {
        public WriteConditionCapabilityDescriptor DescribeWriteConditions() =>
            WriteConditionCapabilityDescriptor.Supporting("ConditionalStore", WriteConditionKind.SingleEventUniqueKey);
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
