namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Forwards reads from a restore source while copying exactly the consumed bytes to a caller-owned destination.
///     It is used only inside the streaming restore seam to obtain independent safe and unsafe payload instances without
///     a full in-memory serialization clone. Disposal never closes either underlying stream.
/// </summary>
internal sealed class SnapshotRestoreTeeReadStream(Stream source, Stream copyDestination) : Stream
{
    public override bool CanRead => source.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException("Streaming restore source length is not available.");
    public override long Position
    {
        get => throw new NotSupportedException("Streaming restore source position is not available.");
        set => throw new NotSupportedException("Streaming restore source position is not available.");
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = source.Read(buffer);
        if (read > 0)
        {
            copyDestination.Write(buffer[..read]);
        }

        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read > 0)
        {
            await copyDestination.WriteAsync(buffer[..read], cancellationToken).ConfigureAwait(false);
        }

        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException("Streaming restore source is non-seekable.");
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        // The actor owns the temporary copy and the resolver caller owns the source. Do not dispose either here.
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync() => base.DisposeAsync();
}
