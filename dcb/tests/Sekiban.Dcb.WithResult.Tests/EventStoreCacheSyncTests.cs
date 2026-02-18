using System.Text;
using Dcb.Domain;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests;

public class EventStoreCacheSyncTests
{
    [Fact]
    public async Task SyncAsync_UsesBatchSize_WhenReadingSerializableEvents()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cache-sync-{Guid.NewGuid():N}.db");
        try
        {
            var events = CreateSerializableEvents(7);
            var remoteStore = new FakeSerializableRemoteStore(events);
            var localStore = new SqliteEventStore(dbPath, DomainType.GetDomainTypes().EventTypes);
            var sync = new EventStoreCacheSync(
                localStore,
                remoteStore,
                new CacheSyncOptions { BatchSize = 3, SafeWindow = TimeSpan.Zero });

            var result = await sync.SyncAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(7, result.EventsSynced);
            Assert.Equal(7, result.TotalEventsInCache);
            Assert.Equal(new int?[] { 3, 3, 3 }, remoteStore.ReadAllSerializableMaxCounts.ToArray());
            Assert.Equal(7L, (await localStore.GetEventCountAsync()).GetValue());
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task SyncAsync_PersistsProgress_PerBatch_WhenRemoteFailsMidway()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cache-sync-{Guid.NewGuid():N}.db");
        try
        {
            var events = CreateSerializableEvents(5);
            var failingRemote = new FakeSerializableRemoteStore(events, failAtReadCall: 2);
            var localStore = new SqliteEventStore(dbPath, DomainType.GetDomainTypes().EventTypes);
            var options = new CacheSyncOptions
            {
                BatchSize = 2,
                SafeWindow = TimeSpan.Zero,
                RemoteEndpoint = "test-remote",
                DatabaseName = "test-db"
            };
            var failingSync = new EventStoreCacheSync(localStore, failingRemote, options);

            var firstResult = await failingSync.SyncAsync();
            Assert.False(firstResult.IsSuccess);
            Assert.Equal(2L, (await localStore.GetEventCountAsync()).GetValue());

            var metadataAfterFailure = await localStore.GetMetadataAsync();
            Assert.NotNull(metadataAfterFailure);
            Assert.Equal(events[1].SortableUniqueIdValue, metadataAfterFailure!.LastCachedSortableUniqueId);

            var succeedingRemote = new FakeSerializableRemoteStore(events);
            var succeedingSync = new EventStoreCacheSync(localStore, succeedingRemote, options);
            var secondResult = await succeedingSync.SyncAsync();

            Assert.True(secondResult.IsSuccess);
            Assert.Equal(3, secondResult.EventsSynced);
            Assert.Equal(5, secondResult.TotalEventsInCache);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private static List<SerializableEvent> CreateSerializableEvents(int count)
    {
        var baseTime = DateTime.UtcNow.AddHours(-1);
        var result = new List<SerializableEvent>(count);
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            var sortableUniqueId = SortableUniqueId.Generate(baseTime.AddSeconds(i), id);
            result.Add(new SerializableEvent(
                Encoding.UTF8.GetBytes($"{{\"index\":{i}}}"),
                sortableUniqueId,
                id,
                new EventMetadata("causation", "correlation", "tester"),
                new List<string> { $"test:{i}" },
                "StudentCreated"));
        }

        return result.OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal).ToList();
    }

    private sealed class FakeSerializableRemoteStore : IEventStore
    {
        private readonly List<SerializableEvent> _events;
        private readonly int? _failAtReadCall;
        private int _readCallCount;
        private readonly List<int?> _maxCounts = new();

        public FakeSerializableRemoteStore(IEnumerable<SerializableEvent> events, int? failAtReadCall = null)
        {
            _events = events.OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal).ToList();
            _failAtReadCall = failAtReadCall;
        }

        public IReadOnlyList<int?> ReadAllSerializableMaxCounts => _maxCounts;

        public Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null, int? maxCount = null) =>
            throw new NotSupportedException();

        public Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
            throw new NotSupportedException();

        public Task<ResultBox<Event>> ReadEventAsync(Guid eventId) =>
            throw new NotSupportedException();

        public Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(
            IEnumerable<Event> events) => throw new NotSupportedException();

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) =>
            throw new NotSupportedException();

        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) =>
            throw new NotSupportedException();

        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) =>
            throw new NotSupportedException();

        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null)
        {
            var count = since == null
                ? _events.Count
                : _events.Count(e => string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
            return Task.FromResult(ResultBox.FromValue((long)count));
        }

        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) =>
            throw new NotSupportedException();

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            ReadAllSerializableEventsAsync(since, null);

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount)
        {
            _readCallCount++;
            _maxCounts.Add(maxCount);

            if (_failAtReadCall.HasValue && _readCallCount == _failAtReadCall.Value)
            {
                return Task.FromResult(ResultBox.Error<IEnumerable<SerializableEvent>>(
                    new InvalidOperationException("Simulated remote failure")));
            }

            IEnumerable<SerializableEvent> query = _events;
            if (since != null)
            {
                query = query.Where(e => string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
            }

            if (maxCount.HasValue)
            {
                query = query.Take(maxCount.Value);
            }

            return Task.FromResult(ResultBox.FromValue(query));
        }

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
            throw new NotSupportedException();

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
            IEnumerable<SerializableEvent> events) => throw new NotSupportedException();
    }
}
