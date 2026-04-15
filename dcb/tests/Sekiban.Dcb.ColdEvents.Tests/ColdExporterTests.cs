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
    public async Task ExportIncrementalAsync_should_create_new_segment_without_reloading_previous_segment()
    {
        // Given
        var options = EnabledOptions with { SegmentMaxEvents = 3, SegmentMaxBytes = long.MaxValue };
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
        Assert.Equal(2, manifest!.Segments.Count);
        Assert.Equal(firstPath, manifest.Segments[0].Path);
        Assert.Equal(2, manifest.Segments[0].EventCount);
        Assert.Equal(1, manifest.Segments[1].EventCount);
        Assert.Equal(e3.SortableUniqueIdValue, manifest.Segments[1].ToSortableUniqueId);
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

        public Task<ResultBox<string>> GetMaxTagInTagGroupAsync(string tagGroup)
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

        public Task<ResultBox<string>> GetMaxTagInTagGroupAsync(string tagGroup)
            => throw new NotSupportedException();
    }
}
