using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Default <see cref="ISnapshotPayloadBufferProvider" /> used by the stream-first
///     multi-projection snapshot persistence path.
///     Returns a buffer backed by a <see cref="SpillableSnapshotPayloadStream" />, which
///     holds the serialized payload in a small in-memory buffer and transparently spills to
///     a temp file once the configured threshold is exceeded. This guarantees that a very
///     large snapshot payload never has to materialize as a contiguous managed
///     <see cref="byte" />[] on the persistence hot path.
/// </summary>
public sealed class SpillableSnapshotPayloadBufferProvider : ISnapshotPayloadBufferProvider
{
    private readonly SpillableSnapshotPayloadOptions _options;
    private readonly ILogger<SpillableSnapshotPayloadBufferProvider> _logger;

    public SpillableSnapshotPayloadBufferProvider(
        SpillableSnapshotPayloadOptions? options = null,
        ILogger<SpillableSnapshotPayloadBufferProvider>? logger = null)
    {
        _options = options ?? new SpillableSnapshotPayloadOptions();
        _logger = logger ?? NullLogger<SpillableSnapshotPayloadBufferProvider>.Instance;
    }

    public Task<ISnapshotPayloadBuffer> CreateBufferAsync(
        string projectorName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = string.IsNullOrWhiteSpace(_options.TempDirectory)
            ? Path.Combine(Path.GetTempPath(), "sekiban-snapshot-payload")
            : _options.TempDirectory;

        var stream = new SpillableSnapshotPayloadStream(
            _options.InMemoryThresholdBytes,
            directory,
            projectorName);

        _logger.LogTrace(
            "Created spillable snapshot payload buffer for {Projector} (threshold={ThresholdBytes} bytes, tempDir={Dir})",
            projectorName,
            _options.InMemoryThresholdBytes,
            directory);

        return Task.FromResult<ISnapshotPayloadBuffer>(new SpillableSnapshotPayloadBuffer(stream));
    }

    private sealed class SpillableSnapshotPayloadBuffer : ISnapshotPayloadBuffer
    {
        private readonly SpillableSnapshotPayloadStream _stream;
        private bool _disposed;

        public SpillableSnapshotPayloadBuffer(SpillableSnapshotPayloadStream stream)
        {
            _stream = stream;
        }

        public Stream Stream => _stream;

        public string Location =>
            _stream.IsSpilled
                ? $"tempfile:{_stream.TempFilePath}"
                : "memory";

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _stream.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
///     Options controlling the spillable snapshot payload buffer.
/// </summary>
public sealed class SpillableSnapshotPayloadOptions
{
    /// <summary>
    ///     Size in bytes of the in-memory buffer used for small snapshot payloads. Once a
    ///     write would push the total buffered size past this threshold, the buffer is
    ///     flushed to a temp file and all subsequent writes go directly to disk.
    ///     Default: 256 KB, which is large enough to keep small projections in memory while
    ///     preventing large projections from holding tens of megabytes of managed byte[].
    /// </summary>
    public int InMemoryThresholdBytes { get; set; } = 256 * 1024;

    /// <summary>
    ///     Directory used for the temp spill file. Defaults to a Sekiban-specific subdirectory
    ///     under <see cref="Path.GetTempPath" /> when left unset.
    /// </summary>
    public string? TempDirectory { get; set; }
}
