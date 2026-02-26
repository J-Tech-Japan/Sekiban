using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for StreamOffloadHelper with seekable streams (e.g. FileStream).
///     After Phase 2 optimization, seekable streams exceeding the threshold
///     are streamed directly to blob without buffering to byte[].
///     The externally observable result is identical to MemoryStream-based tests,
///     but these tests exercise the seekable stream code path.
/// </summary>
public class StreamOffloadHelperSeekableTests : IDisposable
{
    private readonly string _tempDir;

    public StreamOffloadHelperSeekableTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"seekable-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_Seekable_Stream_Below_Threshold_Should_Inline()
    {
        // Given: a seekable FileStream below threshold
        var filePath = Path.Combine(_tempDir, "small.tmp");
        var data = new byte[500];
        Random.Shared.NextBytes(data);
        await File.WriteAllBytesAsync(filePath, data);

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();
        const int threshold = 1000;

        // When
        var result = await StreamOffloadHelper.ProcessAsync(
            fileStream,
            "test-projector/v1",
            threshold,
            blobAccessor,
            CancellationToken.None);

        // Then: data is inlined
        Assert.False(result.IsOffloaded);
        Assert.NotNull(result.InlineData);
        Assert.Equal(data, result.InlineData);
        Assert.Null(result.OffloadKey);
        Assert.Null(result.OffloadProvider);
    }

    [Fact]
    public async Task ProcessAsync_Seekable_Stream_Above_Threshold_Should_Offload()
    {
        // Given: a seekable FileStream above threshold
        var filePath = Path.Combine(_tempDir, "large.tmp");
        var data = new byte[2000];
        Random.Shared.NextBytes(data);
        await File.WriteAllBytesAsync(filePath, data);

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();
        const int threshold = 1000;

        // When
        var result = await StreamOffloadHelper.ProcessAsync(
            fileStream,
            "test-projector/v1",
            threshold,
            blobAccessor,
            CancellationToken.None);

        // Then: data is offloaded
        Assert.True(result.IsOffloaded);
        Assert.Null(result.InlineData);
        Assert.NotNull(result.OffloadKey);
        Assert.Equal(blobAccessor.ProviderName, result.OffloadProvider);
    }

    [Fact]
    public async Task ProcessAsync_Seekable_Stream_Offloaded_Data_Should_Be_Retrievable()
    {
        // Given: a seekable FileStream that will be offloaded
        var filePath = Path.Combine(_tempDir, "retrievable.tmp");
        var data = new byte[2000];
        Random.Shared.NextBytes(data);
        await File.WriteAllBytesAsync(filePath, data);

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();
        const int threshold = 1000;

        // When
        var result = await StreamOffloadHelper.ProcessAsync(
            fileStream,
            "test-projector/v1",
            threshold,
            blobAccessor,
            CancellationToken.None);

        // Then: offloaded data can be read back and matches original
        Assert.True(result.IsOffloaded);
        Assert.NotNull(result.OffloadKey);
        await using var readStream = await blobAccessor.OpenReadAsync(result.OffloadKey);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);
        var readBack = ms.ToArray();
        Assert.Equal(data, readBack);
    }

    [Fact]
    public async Task ProcessAsync_Seekable_Stream_At_Exact_Threshold_Should_Inline()
    {
        // Given: a seekable FileStream at exact threshold boundary
        const int threshold = 1000;
        var filePath = Path.Combine(_tempDir, "exact.tmp");
        var data = new byte[threshold];
        Random.Shared.NextBytes(data);
        await File.WriteAllBytesAsync(filePath, data);

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();

        // When
        var result = await StreamOffloadHelper.ProcessAsync(
            fileStream,
            "test-projector/v1",
            threshold,
            blobAccessor,
            CancellationToken.None);

        // Then: exact threshold is not exceeded, so inline
        Assert.False(result.IsOffloaded);
        Assert.NotNull(result.InlineData);
        Assert.Equal(data, result.InlineData);
    }

    [Fact]
    public async Task ProcessAsync_Seekable_Stream_Without_BlobAccessor_Should_Inline()
    {
        // Given: a seekable FileStream above threshold but no blob accessor
        var filePath = Path.Combine(_tempDir, "no-blob.tmp");
        var data = new byte[2000];
        Random.Shared.NextBytes(data);
        await File.WriteAllBytesAsync(filePath, data);

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        const int threshold = 1000;

        // When
        var result = await StreamOffloadHelper.ProcessAsync(
            fileStream,
            "test-projector/v1",
            threshold,
            blobAccessor: null,
            CancellationToken.None);

        // Then: forced to inline since no blob accessor
        Assert.False(result.IsOffloaded);
        Assert.NotNull(result.InlineData);
        Assert.Equal(data, result.InlineData);
        Assert.Null(result.OffloadKey);
        Assert.Null(result.OffloadProvider);
    }

    [Fact]
    public async Task ProcessAsync_Seekable_Stream_One_Byte_Over_Threshold_Should_Offload()
    {
        // Given: a seekable FileStream one byte over threshold
        const int threshold = 1000;
        var filePath = Path.Combine(_tempDir, "one-over.tmp");
        var data = new byte[threshold + 1];
        Random.Shared.NextBytes(data);
        await File.WriteAllBytesAsync(filePath, data);

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();

        // When
        var result = await StreamOffloadHelper.ProcessAsync(
            fileStream,
            "test-projector/v1",
            threshold,
            blobAccessor,
            CancellationToken.None);

        // Then: exceeds threshold, offloaded
        Assert.True(result.IsOffloaded);
        Assert.Null(result.InlineData);
        Assert.NotNull(result.OffloadKey);
    }

    [Fact]
    public async Task ProcessAsync_Seekable_Empty_FileStream_Should_Inline()
    {
        // Given: an empty seekable FileStream
        var filePath = Path.Combine(_tempDir, "empty.tmp");
        await File.WriteAllBytesAsync(filePath, Array.Empty<byte>());

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();
        const int threshold = 1000;

        // When
        var result = await StreamOffloadHelper.ProcessAsync(
            fileStream,
            "test-projector/v1",
            threshold,
            blobAccessor,
            CancellationToken.None);

        // Then: empty data is inlined
        Assert.False(result.IsOffloaded);
        Assert.NotNull(result.InlineData);
        Assert.Empty(result.InlineData);
    }
}
