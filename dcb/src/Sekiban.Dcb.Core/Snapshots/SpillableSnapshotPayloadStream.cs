using System.Buffers;

namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     A seekable, writable stream that starts buffered in a small <see cref="MemoryStream" /> and
///     transparently spills over to a temp file once the in-memory buffer exceeds a configurable
///     threshold. Used by the stream-first multi-projection snapshot persistence path so that a
///     very large serialized payload never needs to live as a contiguous managed
///     <see cref="byte" />[] on the hot path.
///     The stream mirrors each write into whichever backing store is active, switching backing
///     stores exactly once (memory -> disk). Dispose guarantees the temp file is deleted.
/// </summary>
internal sealed class SpillableSnapshotPayloadStream : Stream
{
    private const int CopyChunkSize = 81920;

    private readonly int _spillThresholdBytes;
    private readonly string _tempFileDirectory;
    private readonly string _projectorName;

    private MemoryStream? _memory;
    private FileStream? _file;
    private string? _filePath;
    private long _length;
    private long _position;
    private bool _disposed;

    /// <summary>
    ///     Creates a new spillable stream.
    /// </summary>
    /// <param name="spillThresholdBytes">
    ///     Once the buffered byte count exceeds this threshold, the buffer is flushed to a
    ///     temp file and all subsequent writes go directly to the file. Must be &gt;= 0.
    ///     A value of 0 effectively forces immediate spill on first write.
    /// </param>
    /// <param name="tempFileDirectory">Directory used for the spill file. Created if missing.</param>
    /// <param name="projectorName">Logical projector name embedded in the temp file name for diagnostics.</param>
    public SpillableSnapshotPayloadStream(
        int spillThresholdBytes,
        string tempFileDirectory,
        string projectorName)
    {
        if (spillThresholdBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spillThresholdBytes));
        }

        _spillThresholdBytes = spillThresholdBytes;
        _tempFileDirectory = tempFileDirectory ?? throw new ArgumentNullException(nameof(tempFileDirectory));
        _projectorName = string.IsNullOrWhiteSpace(projectorName) ? "unknown" : projectorName;

        _memory = new MemoryStream();
    }

    /// <summary>
    ///     True after the backing store has been switched from memory to a temp file.
    /// </summary>
    public bool IsSpilled => _file is not null;

    /// <summary>
    ///     Path of the temp file used for spillover, or <c>null</c> when still in memory.
    /// </summary>
    public string? TempFilePath => _filePath;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override void Flush() => _file?.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _file is not null ? _file.FlushAsync(cancellationToken) : Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_file is not null)
        {
            _file.Position = _position;
            var read = _file.Read(buffer, offset, count);
            _position += read;
            return read;
        }

        _memory!.Position = _position;
        var memRead = _memory.Read(buffer, offset, count);
        _position += memRead;
        return memRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (newPosition < 0)
        {
            throw new IOException("Cannot seek before stream start");
        }

        _position = newPosition;
        return _position;
    }

    public override void SetLength(long value)
    {
        EnsureBacking(value);
        if (_file is not null)
        {
            _file.SetLength(value);
        }
        else
        {
            _memory!.SetLength(value);
        }

        _length = value;
        if (_position > value)
        {
            _position = value;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (count == 0)
        {
            return;
        }

        EnsureBacking(_length + count);

        if (_file is not null)
        {
            _file.Position = _position;
            _file.Write(buffer, offset, count);
        }
        else
        {
            _memory!.Position = _position;
            _memory.Write(buffer, offset, count);
        }

        _position += count;
        if (_position > _length)
        {
            _length = _position;
        }
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        EnsureBacking(_length + buffer.Length);

        if (_file is not null)
        {
            _file.Position = _position;
            _file.Write(buffer);
        }
        else
        {
            _memory!.Position = _position;
            _memory.Write(buffer);
        }

        _position += buffer.Length;
        if (_position > _length)
        {
            _length = _position;
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        EnsureBacking(_length + buffer.Length);

        if (_file is not null)
        {
            _file.Position = _position;
            await _file.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _memory!.Position = _position;
            await _memory.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        _position += buffer.Length;
        if (_position > _length)
        {
            _length = _position;
        }
    }

    public override void WriteByte(byte value)
    {
        EnsureBacking(_length + 1);
        if (_file is not null)
        {
            _file.Position = _position;
            _file.WriteByte(value);
        }
        else
        {
            _memory!.Position = _position;
            _memory.WriteByte(value);
        }

        _position++;
        if (_position > _length)
        {
            _length = _position;
        }
    }

    /// <summary>
    ///     Ensures the backing store can hold the requested total length. If the new total
    ///     exceeds the spill threshold and we are still in memory, the buffered content is
    ///     flushed to a temp file and the file takes over as the backing store.
    /// </summary>
    private void EnsureBacking(long requiredTotalLength)
    {
        if (_file is not null)
        {
            return;
        }

        if (requiredTotalLength <= _spillThresholdBytes)
        {
            return;
        }

        SpillToTempFile();
    }

    private void SpillToTempFile()
    {
        if (_file is not null)
        {
            return;
        }

        Directory.CreateDirectory(_tempFileDirectory);
        var fileName = BuildTempFileName();
        var filePath = Path.Combine(_tempFileDirectory, fileName);
        var file = new FileStream(
            filePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: CopyChunkSize,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        _filePath = filePath;
        _file = file;

        if (_memory is not null)
        {
            if (_memory.TryGetBuffer(out var segment) && segment.Count > 0)
            {
                _file.Write(segment.Array!, segment.Offset, segment.Count);
            }
            else
            {
                _memory.Position = 0;
                CopyMemoryToFile();
            }

            _memory.Dispose();
            _memory = null;
        }
    }

    private void CopyMemoryToFile()
    {
        // Fallback copy path for memory streams that do not expose TryGetBuffer.
        var rented = ArrayPool<byte>.Shared.Rent(CopyChunkSize);
        try
        {
            int read;
            while ((read = _memory!.Read(rented, 0, rented.Length)) > 0)
            {
                _file!.Write(rented, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private string BuildTempFileName()
    {
        var safe = new System.Text.StringBuilder(_projectorName.Length);
        foreach (var ch in _projectorName)
        {
            safe.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
        }
        return $"sekiban-snapshot-spill-{safe}-{Guid.NewGuid():N}.bin";
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            if (_memory is not null)
            {
                _memory.Dispose();
                _memory = null;
            }
            if (_file is not null)
            {
                // DeleteOnClose takes care of the temp file itself.
                try
                {
                    _file.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
                _file = null;
            }
            // Defensive cleanup in case DeleteOnClose didn't remove the file
            // (for example, if the stream was never successfully written to).
            if (_filePath is not null)
            {
                try
                {
                    if (File.Exists(_filePath))
                    {
                        File.Delete(_filePath);
                    }
                }
                catch
                {
                    // Best-effort cleanup.
                }
                _filePath = null;
            }
        }

        _disposed = true;
        base.Dispose(disposing);
    }
}
