using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.ColdEvents.Tests;

/// <summary>
///     A decorator must report where the data actually lands, not what wrapped it. This is the whole reason the
///     descriptor is resolved from the live instance: if a wrapper could claim durability on its own authority, the
///     guard would be checking the wrapper's opinion of itself.
/// </summary>
public class HybridEventStoreDurabilityDescriptorTests
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
    public void WrappingAVolatileStore_StaysVolatile()
    {
        var descriptor = Wrap(new InMemoryEventStore()).DescribeStorage();

        // The dangerous direction: a durable-sounding decorator over a volatile store is still data loss.
        Assert.Equal(StorageDurability.Volatile, descriptor.Durability);
        Assert.Equal("HybridEventStore(InMemory)", descriptor.ProviderName);
    }

    [Fact]
    public void WrappingADurableStore_StaysDurable()
    {
        var descriptor = Wrap(new DescribingStore(new StorageDurabilityDescriptor(StorageDurability.Durable, "TestDurable")))
            .DescribeStorage();

        Assert.Equal(StorageDurability.Durable, descriptor.Durability);
        Assert.Equal("HybridEventStore(TestDurable)", descriptor.ProviderName);
    }

    [Fact]
    public void WrappingAStoreThatSaysNothing_StaysUnknown()
    {
        // Silence does not become durability by being wrapped. Every third-party store starts out this shape.
        var descriptor = Wrap(new SilentStore()).DescribeStorage();

        Assert.Equal(StorageDurability.Unknown, descriptor.Durability);
        Assert.Equal("HybridEventStore(SilentStore)", descriptor.ProviderName);
    }

    /// <summary>An event store that answers the durability question however the test tells it to.</summary>
    private sealed class DescribingStore(StorageDurabilityDescriptor descriptor)
        : SilentStore, IStorageDurabilityDescriptorProvider
    {
        public StorageDurabilityDescriptor DescribeStorage() => descriptor;
    }

    /// <summary>
    ///     An event store that does not implement the descriptor at all. None of these members are ever reached:
    ///     describing a store does not read from it.
    /// </summary>
    private class SilentStore : IEventStore
    {
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) =>
            throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) =>
            throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount) => throw new NotSupportedException();
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) =>
            throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => throw new NotSupportedException();
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();
    }
}
