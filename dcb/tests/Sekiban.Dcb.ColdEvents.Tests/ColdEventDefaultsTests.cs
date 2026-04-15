using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.ColdEvents.Tests;

public class ColdEventDefaultsTests
{
    [Fact]
    public void AddSekibanDcbColdEventDefaults_should_register_runner_dependencies()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbColdEventDefaults();

        Assert.Contains(services, d => d.ServiceType == typeof(ColdExportCycleRunner));
        Assert.Contains(services, d => d.ServiceType == typeof(IOptions<ColdEventStoreOptions>));
        Assert.Contains(services, d => d.ServiceType == typeof(IServiceIdProvider));
    }

    [Fact]
    public void AddSekibanDcbColdEventHybridRead_should_keep_hot_store_separate_from_decorated_store()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHotEventStore, StubHotEventStore>();
        services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<IHotEventStore>());
        services.AddSingleton<IColdObjectStorage, InMemoryColdObjectStorage>();
        services.AddSingleton<IColdSegmentFormatHandler, JsonlColdSegmentFormatHandler>();
        services.AddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
        services.AddSingleton<IOptions<ColdEventStoreOptions>>(Options.Create(new ColdEventStoreOptions
        {
            Enabled = true
        }));
        services.AddSingleton<ILogger<HybridEventStore>>(NullLogger<HybridEventStore>.Instance);

        services.AddSekibanDcbColdEventHybridRead();

        using var serviceProvider = services.BuildServiceProvider();
        var hotStore = serviceProvider.GetRequiredService<IHotEventStore>();
        var eventStore = serviceProvider.GetRequiredService<IEventStore>();

        Assert.IsType<StubHotEventStore>(hotStore);
        Assert.IsType<HybridEventStore>(eventStore);
        Assert.NotSame(hotStore, eventStore);
    }

    private sealed class StubHotEventStore : IHotEventStore
    {
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => throw new NotSupportedException();
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) => throw new NotSupportedException();
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => throw new NotSupportedException();
    }
}
