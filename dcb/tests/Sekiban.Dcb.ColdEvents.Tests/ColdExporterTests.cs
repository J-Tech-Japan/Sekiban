using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.ColdEvents.Tests;

public class ColdExporterTests
{
    private const string ServiceId = "test-service";

    private static readonly ColdEventStoreOptions EnabledOptions = new()
    {
        Enabled = true,
        PullInterval = TimeSpan.FromMinutes(30),
        SafeWindow = TimeSpan.FromMinutes(2),
        SegmentMaxEvents = 100_000,
        SegmentMaxBytes = 512L * 1024 * 1024
    };

    private readonly InMemoryColdObjectStorage _storage = new();
    private readonly InMemoryColdLeaseManager _leaseManager = new();
    private readonly IColdSegmentFormatHandler _segmentFormatHandler = new JsonlColdSegmentFormatHandler();

    private static SerializableEvent CreateEvent(DateTime timestamp, string name)
    {
        var sortableId = SortableUniqueId.Generate(timestamp, Guid.NewGuid());
        return new SerializableEvent(
            Payload: [1, 2, 3],
            SortableUniqueIdValue: sortableId,
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata("cause", "corr", "user"),
            Tags: ["tag1"],
            EventPayloadName: name);
    }

    private static SerializableEvent[] CreateEvents(DateTime start, int count, string prefix)
        => Enumerable.Range(0, count)
            .Select(i => CreateEvent(start.AddSeconds(i), $"{prefix}{i + 1}"))
            .ToArray();

    private ColdExporter CreateExporter(
        IHotEventStore hotStore,
        ColdEventStoreOptions? options = null,
        IColdObjectStorage? storage = null,
        IColdLeaseManager? leaseManager = null)
    {
        return new ColdExporter(
            hotStore,
            storage ?? _storage,
            _segmentFormatHandler,
            leaseManager ?? _leaseManager,
            Options.Create(options ?? EnabledOptions),
            NullLogger<ColdExporter>.Instance);
    }

    [Fact]
    public async Task GetStatusAsync_should_return_supported_and_enabled()
    {
        // Given
        var exporter = CreateExporter(new StubEventStore([]));

        // When
        var status = await exporter.GetStatusAsync(CancellationToken.None);

        // Then
        Assert.True(status.IsSupported);
        Assert.True(status.IsEnabled);
    }

    [Fact]
    public async Task GetStatusAsync_should_return_supported_but_disabled_when_not_enabled()
    {
        // Given
        var options = new ColdEventStoreOptions { Enabled = false };
        var exporter = CreateExporter(new StubEventStore([]), options);

        // When
        var status = await exporter.GetStatusAsync(CancellationToken.None);

        // Then
        Assert.True(status.IsSupported);
        Assert.False(status.IsEnabled);
    }

    [Fact]
    public async Task ExportIncrementalAsync_should_return_zero_when_no_events()
    {
        // Given
        var exporter = CreateExporter(new StubEventStore([]));

        // When
        var result = await exporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.GetValue().ExportedEventCount);
    }

    [Fact]
    public async Task ExportIncrementalAsync_should_return_error_when_disabled()
    {
        // Given
        var options = new ColdEventStoreOptions { Enabled = false };
        var exporter = CreateExporter(new StubEventStore([]), options);

        // When
        var result = await exporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);

        // Then
        Assert.False(result.IsSuccess);
        Assert.IsType<InvalidOperationException>(result.GetException());
    }

    [Fact]
    public async Task ExportIncrementalAsync_should_export_safe_events_only()
    {
        // Given: 2 safe events (old) + 1 unsafe event (now)
        var safeTime = DateTime.UtcNow.AddMinutes(-10);
        var unsafeTime = DateTime.UtcNow;
        var events = new[]
        {
            CreateEvent(safeTime, "Event1"),
            CreateEvent(safeTime.AddSeconds(1), "Event2"),
            CreateEvent(unsafeTime, "Event3")
        };
        var exporter = CreateExporter(new StubEventStore(events));

        // When
        var result = await exporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.GetValue().ExportedEventCount);
        Assert.Single(result.GetValue().NewSegments);
    }

    [Fact]
    public async Task ExportIncrementalAsync_should_skip_when_lease_held()
    {
        // Given: lease already held
        await _leaseManager.AcquireAsync($"cold-export-{ServiceId}", TimeSpan.FromHours(1), CancellationToken.None);
        var events = new[] { CreateEvent(DateTime.UtcNow.AddMinutes(-10), "Event1") };
        var exporter = CreateExporter(new StubEventStore(events));

        // When
        var result = await exporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);

        // Then: should return 0 exported (skipped)
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.GetValue().ExportedEventCount);
    }

    [Fact]
    public async Task ExportIncrementalAsync_should_create_manifest_and_checkpoint()
    {
        // Given
        var safeTime = DateTime.UtcNow.AddMinutes(-10);
        var events = new[] { CreateEvent(safeTime, "Event1") };
        var exporter = CreateExporter(new StubEventStore(events));

        // When
        await exporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);

        // Then: manifest should be readable
        var progressResult = await exporter.GetProgressAsync(ServiceId, CancellationToken.None);
        Assert.True(progressResult.IsSuccess);
        var progress = progressResult.GetValue();
        Assert.NotNull(progress.LatestSafeSortableUniqueId);
        Assert.NotNull(progress.NextSinceSortableUniqueId);
    }

    [Fact]
    public async Task ExportIncrementalAsync_should_merge_incremental_events_into_existing_tail_when_tail_has_capacity()
    {
        // Given
        var options = EnabledOptions with { SegmentMaxEvents = 10, SegmentMaxBytes = long.MaxValue };
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        var e1 = CreateEvent(t0, "Event1");
        var e2 = CreateEvent(t0.AddSeconds(1), "Event2");
        var e3 = CreateEvent(t0.AddSeconds(2), "Event3");

        var firstExporter = CreateExporter(new StubEventStore([e1, e2]), options, _storage, _leaseManager);

        // When: first export creates one segment with 2 events
        var first = await firstExporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);

        // Then
        Assert.True(first.IsSuccess);
        Assert.Single(first.GetValue().NewSegments);
        var firstPath = first.GetValue().NewSegments[0].Path;

        var secondExporter = CreateExporter(new StubEventStore([e1, e2, e3]), options, _storage, _leaseManager);
        var second = await secondExporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);

        Assert.True(second.IsSuccess);
        var manifest = await ColdControlFileHelper.LoadManifestAsync(_storage, ServiceId, CancellationToken.None);
        Assert.NotNull(manifest);
        Assert.Single(manifest!.Segments);
        Assert.NotEqual(firstPath, manifest.Segments[0].Path);
        Assert.Equal(3, manifest.Segments[0].EventCount);
        Assert.Equal(e1.SortableUniqueIdValue, manifest.Segments[0].FromSortableUniqueId);
        Assert.Equal(e3.SortableUniqueIdValue, manifest.Segments[0].ToSortableUniqueId);
    }

    [Fact]
    public async Task ExportIncrementalAsync_should_rotate_to_new_segment_when_latest_is_full()
    {
        // Given
        var options = EnabledOptions with { SegmentMaxEvents = 2, SegmentMaxBytes = long.MaxValue };
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        var e1 = CreateEvent(t0, "Event1");
        var e2 = CreateEvent(t0.AddSeconds(1), "Event2");
        var e3 = CreateEvent(t0.AddSeconds(2), "Event3");

        var firstExporter = CreateExporter(new StubEventStore([e1, e2]), options, _storage, _leaseManager);
        var first = await firstExporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);
        Assert.True(first.IsSuccess);

        var secondExporter = CreateExporter(new StubEventStore([e1, e2, e3]), options, _storage, _leaseManager);
        var second = await secondExporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);
        Assert.True(second.IsSuccess);

        var manifest = await ColdControlFileHelper.LoadManifestAsync(_storage, ServiceId, CancellationToken.None);
        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.Segments.Count);
        Assert.Equal(2, manifest.Segments[0].EventCount);
        Assert.Equal(1, manifest.Segments[1].EventCount);
        Assert.Equal(e3.SortableUniqueIdValue, manifest.Segments[1].ToSortableUniqueId);
    }

    [Fact]
    public async Task ExportIncrementalAsync_should_not_grow_tiny_tail_segments_for_repeated_trickle_exports()
    {
        // Given
        var options = EnabledOptions with { SegmentMaxEvents = 16, SegmentMaxBytes = long.MaxValue };
        var t0 = DateTime.UtcNow.AddMinutes(-20);
        var initialEvents = CreateEvents(t0, 10, "Initial");
        var trickleOne = CreateEvents(t0.AddMinutes(1), 3, "TrickleA");
        var trickleTwo = CreateEvents(t0.AddMinutes(2), 5, "TrickleB");
        var trickleThree = CreateEvents(t0.AddMinutes(3), 8, "TrickleC");

        var firstExporter = CreateExporter(new StubEventStore(initialEvents), options, _storage, _leaseManager);
        var first = await firstExporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);
        Assert.True(first.IsSuccess);

        var secondExporter = CreateExporter(
            new StubEventStore(initialEvents.Concat(trickleOne).ToArray()),
            options,
            _storage,
            _leaseManager);
        var second = await secondExporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);
        Assert.True(second.IsSuccess);
        var secondManifest = await ColdControlFileHelper.LoadManifestAsync(_storage, ServiceId, CancellationToken.None);
        Assert.NotNull(secondManifest);
        Assert.Single(secondManifest!.Segments);
        Assert.Equal(13, secondManifest.Segments[0].EventCount);

        var thirdExporter = CreateExporter(
            new StubEventStore(initialEvents.Concat(trickleOne).Concat(trickleTwo).ToArray()),
            options,
            _storage,
            _leaseManager);
        var third = await thirdExporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);
        Assert.True(third.IsSuccess);
        var thirdManifest = await ColdControlFileHelper.LoadManifestAsync(_storage, ServiceId, CancellationToken.None);
        Assert.NotNull(thirdManifest);
        Assert.Equal(2, thirdManifest!.Segments.Count);
        Assert.Equal(13, thirdManifest.Segments[0].EventCount);
        Assert.Equal(5, thirdManifest.Segments[1].EventCount);

        var fourthExporter = CreateExporter(
            new StubEventStore(initialEvents.Concat(trickleOne).Concat(trickleTwo).Concat(trickleThree).ToArray()),
            options,
            _storage,
            _leaseManager);
        var fourth = await fourthExporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);
        Assert.True(fourth.IsSuccess);

        var manifest = await ColdControlFileHelper.LoadManifestAsync(_storage, ServiceId, CancellationToken.None);
        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.Segments.Count);
        Assert.Equal(13, manifest.Segments[0].EventCount);
        Assert.Equal(13, manifest.Segments[1].EventCount);
    }

    [Fact]
    public async Task ExportIncrementalAsync_should_retry_tail_merge_after_manifest_conflict()
    {
        // Given
        var options = EnabledOptions with { SegmentMaxEvents = 10, SegmentMaxBytes = long.MaxValue };
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        var e1 = CreateEvent(t0, "Event1");
        var e2 = CreateEvent(t0.AddSeconds(1), "Event2");
        var e3 = CreateEvent(t0.AddSeconds(2), "Event3");

        var baseStorage = new InMemoryColdObjectStorage();
        var flakyStorage = new FailFirstManifestWriteStorage(baseStorage, ServiceId);

        var firstExporter = CreateExporter(new StubEventStore([e1, e2]), options, flakyStorage, _leaseManager);
        var first = await firstExporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);
        Assert.True(first.IsSuccess);

        var secondExporter = CreateExporter(new StubEventStore([e1, e2, e3]), options, flakyStorage, _leaseManager);
        var second = await secondExporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);

        Assert.True(second.IsSuccess);
        var manifest = await ColdControlFileHelper.LoadManifestAsync(flakyStorage, ServiceId, CancellationToken.None);
        Assert.NotNull(manifest);
        Assert.Single(manifest!.Segments);
        Assert.Equal(3, manifest.Segments[0].EventCount);
        Assert.Equal(e3.SortableUniqueIdValue, manifest.Segments[0].ToSortableUniqueId);
    }

    [Fact]
    public async Task ExportIncrementalAsync_should_fail_when_checkpoint_write_fails()
    {
        // Given
        var safeTime = DateTime.UtcNow.AddMinutes(-10);
        var events = new[] { CreateEvent(safeTime, "Event1") };
        var innerStorage = new InMemoryColdObjectStorage();
        var failingStorage = new FailingCheckpointStorage(innerStorage, ServiceId);
        var exporter = CreateExporter(new StubEventStore(events), storage: failingStorage);

        // When
        var result = await exporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);

        // Then
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ExportIncrementalAsync_should_use_stream_reader_when_available()
    {
        var safeTime = DateTime.UtcNow.AddMinutes(-10);
        var events = new[]
        {
            CreateEvent(safeTime, "Event1"),
            CreateEvent(safeTime.AddSeconds(1), "Event2")
        };
        var exporter = CreateExporter(new StreamOnlyStubEventStore(events));

        var result = await exporter.ExportIncrementalAsync(ServiceId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.GetValue().ExportedEventCount);
        Assert.Single(result.GetValue().NewSegments);
    }

    [Fact]
    public async Task GetProgressAsync_should_return_default_when_no_manifest()
    {
        // Given
        var exporter = CreateExporter(new StubEventStore([]));

        // When
        var result = await exporter.GetProgressAsync(ServiceId, CancellationToken.None);

        // Then
        Assert.True(result.IsSuccess);
        var progress = result.GetValue();
        Assert.Equal(ServiceId, progress.ServiceId);
        Assert.Null(progress.LatestSafeSortableUniqueId);
        Assert.Equal("0", progress.ManifestVersion);
    }

    private sealed class StubEventStore : IHotEventStore
    {
        private readonly IReadOnlyList<SerializableEvent> _events;

        public StubEventStore(IReadOnlyList<SerializableEvent> events)
        {
            _events = events;
        }

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
            => throw new NotSupportedException();

        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
            => throw new NotSupportedException();

        public Task<ResultBox<bool>> TagExistsAsync(ITag tag)
            => throw new NotSupportedException();

        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null)
            => throw new NotSupportedException();

        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null)
            => throw new NotSupportedException();

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync()
            => throw new NotSupportedException();

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null)
        {
            IEnumerable<SerializableEvent> result = since is null
                ? _events
                : _events.Where(e =>
                    string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
            return Task.FromResult(ResultBox.FromValue(result));
        }

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount)
        {
            IEnumerable<SerializableEvent> result = since is null
                ? _events
                : _events.Where(e =>
                    string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
            if (maxCount is > 0)
            {
                result = result.Take(maxCount.Value);
            }

            return Task.FromResult(ResultBox.FromValue(result));
        }

        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId)
        {
            var result = _events.FirstOrDefault(e => e.Id == eventId);
            return result == null
                ? Task.FromResult(ResultBox.Error<SerializableEvent>(new KeyNotFoundException($"Event {eventId} not found")))
                : Task.FromResult(ResultBox.FromValue(result));
        }

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null)
            => throw new NotSupportedException();

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
            => throw new NotSupportedException();
    }

    private sealed class FailingCheckpointStorage : IColdObjectStorage
    {
        private readonly IColdObjectStorage _inner;
        private readonly string _checkpointPath;

        public FailingCheckpointStorage(IColdObjectStorage inner, string serviceId)
        {
            _inner = inner;
            _checkpointPath = ColdStoragePaths.CheckpointPath(serviceId);
        }

        public Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct)
            => _inner.GetAsync(path, ct);

        public Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct)
        {
            if (string.Equals(path, _checkpointPath, StringComparison.Ordinal))
            {
                return Task.FromResult(ResultBox.Error<bool>(
                    new InvalidOperationException("checkpoint write failed")));
            }
            return _inner.PutAsync(path, data, expectedETag, ct);
        }

        public Task<ResultBox<bool>> PutAsync(string path, Stream data, string? expectedETag, CancellationToken ct)
        {
            if (string.Equals(path, _checkpointPath, StringComparison.Ordinal))
            {
                return Task.FromResult(ResultBox.Error<bool>(
                    new InvalidOperationException("checkpoint write failed")));
            }

            return _inner.PutAsync(path, data, expectedETag, ct);
        }

        public Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct)
            => _inner.ListAsync(prefix, ct);

        public Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct)
            => _inner.DeleteAsync(path, ct);
    }

    private sealed class StreamOnlyStubEventStore : IHotEventStore, ISerializableEventStreamReader
    {
        private readonly IReadOnlyList<SerializableEvent> _events;

        public StreamOnlyStubEventStore(IReadOnlyList<SerializableEvent> events)
        {
            _events = events;
        }

        public IAsyncEnumerable<SerializableEvent> StreamAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount,
            CancellationToken ct = default)
            => ReadAsync(since, maxCount, ct);

        private async IAsyncEnumerable<SerializableEvent> ReadAsync(
            SortableUniqueId? since,
            int? maxCount,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            IEnumerable<SerializableEvent> result = since is null
                ? _events
                : _events.Where(e =>
                    string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);

            if (maxCount is > 0)
            {
                result = result.Take(maxCount.Value);
            }

            foreach (var evt in result)
            {
                ct.ThrowIfCancellationRequested();
                yield return evt;
                await Task.Yield();
            }
        }

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
            => throw new NotSupportedException();

        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
            => throw new NotSupportedException();

        public Task<ResultBox<bool>> TagExistsAsync(ITag tag)
            => throw new NotSupportedException();

        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null)
            => throw new NotSupportedException();

        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null)
            => throw new NotSupportedException();

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync()
            => throw new NotSupportedException();

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null)
            => throw new NotSupportedException("stream path should be used");

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount)
            => throw new NotSupportedException("stream path should be used");

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null)
            => throw new NotSupportedException();

        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId)
        {
            var result = _events.FirstOrDefault(e => e.Id == eventId);
            return result == null
                ? Task.FromResult(ResultBox.Error<SerializableEvent>(new KeyNotFoundException($"Event {eventId} not found")))
                : Task.FromResult(ResultBox.FromValue(result));
        }

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
            => throw new NotSupportedException();
    }

    private sealed class FailFirstManifestWriteStorage : IColdObjectStorage
    {
        private readonly IColdObjectStorage _inner;
        private readonly string _manifestPath;
        private bool _shouldFail = true;

        public FailFirstManifestWriteStorage(IColdObjectStorage inner, string serviceId)
        {
            _inner = inner;
            _manifestPath = ColdStoragePaths.ManifestPath(serviceId);
        }

        public Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct)
            => _inner.GetAsync(path, ct);

        public Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct)
        {
            if (_shouldFail
                && string.Equals(path, _manifestPath, StringComparison.Ordinal)
                && expectedETag is not null)
            {
                _shouldFail = false;
                return Task.FromResult(ResultBox.Error<bool>(
                    new InvalidOperationException("ETag mismatch at manifest")));
            }

            return _inner.PutAsync(path, data, expectedETag, ct);
        }

        public Task<ResultBox<bool>> PutAsync(string path, Stream data, string? expectedETag, CancellationToken ct)
            => _inner.PutAsync(path, data, expectedETag, ct);

        public Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct)
            => _inner.ListAsync(prefix, ct);

        public Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct)
            => _inner.DeleteAsync(path, ct);
    }
}
