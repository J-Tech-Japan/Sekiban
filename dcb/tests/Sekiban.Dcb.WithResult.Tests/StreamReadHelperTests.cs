using System.Text;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.Tests;

public class StreamReadHelperTests
{
    [Fact]
    public async Task ReadAllBytesAsync_MemoryStream_At_Start_Returns_All_Bytes()
    {
        var data = Encoding.UTF8.GetBytes("abcdef");
        using var stream = new MemoryStream(data, writable: false);

        var result = await StreamReadHelper.ReadAllBytesAsync(stream);

        Assert.Equal(data, result);
    }

    [Fact]
    public async Task ReadAllBytesAsync_MemoryStream_With_NonZero_Position_Returns_Remaining_Bytes()
    {
        var data = Encoding.UTF8.GetBytes("abcdef");
        using var stream = new MemoryStream(data, writable: false);
        stream.Position = 2;

        var result = await StreamReadHelper.ReadAllBytesAsync(stream);

        Assert.Equal(Encoding.UTF8.GetBytes("cdef"), result);
    }

    [Fact]
    public async Task ReadAllBytesAsync_Seekable_Stream_With_NonZero_Position_Returns_Remaining_Bytes()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, Encoding.UTF8.GetBytes("0123456789"));
            await using var stream = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            stream.Position = 5;

            var result = await StreamReadHelper.ReadAllBytesAsync(stream);

            Assert.Equal(Encoding.UTF8.GetBytes("56789"), result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadAllBytesAsync_NonSeekable_Stream_Reads_All_Bytes()
    {
        var data = Encoding.UTF8.GetBytes("non-seekable");
        using var stream = new NonSeekableReadOnlyStream(data);

        var result = await StreamReadHelper.ReadAllBytesAsync(stream);

        Assert.Equal(data, result);
    }

    private sealed class NonSeekableReadOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
