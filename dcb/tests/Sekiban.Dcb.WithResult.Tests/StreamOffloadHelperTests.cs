using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Unit tests for StreamOffloadHelper — the shared static helper
///     that decides whether snapshot state data should be inlined or offloaded to blob storage.
/// </summary>
public class StreamOffloadHelperTests
{
    [Fact]
    public async Task ProcessAsync_Should_Return_Inline_When_Stream_Size_Is_Below_Threshold()
    {
        // Given: a 500-byte stream and a 1000-byte threshold
        var data = new byte[500];
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();
        const int threshold = 1000;

        // When
        var result = await StreamOffloadHelper.ProcessAsync(
            stream,
            "test-projector/v1",
            threshold,
            blobAccessor,
            CancellationToken.None);

        // Then: data is inlined, not offloaded
        Assert.False(result.IsOffloaded);
        Assert.NotNull(result.InlineData);
        Assert.Equal(data, result.InlineData);
        Assert.Null(result.OffloadKey);
        Assert.Null(result.OffloadProvider);
    }

    [Fact]
    public async Task ProcessAsync_Should_Offload_When_Stream_Size_Exceeds_Threshold()
    {
        // Given: a 2000-byte stream and a 1000-byte threshold
        var data = new byte[2000];
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();
        const int threshold = 1000;

        // When
        var result = await StreamOffloadHelper.ProcessAsync(
            stream,
            "test-projector/v1",
            threshold,
            blobAccessor,
            CancellationToken.None);

        // Then: data is offloaded to blob
        Assert.True(result.IsOffloaded);
        Assert.Null(result.InlineData);
        Assert.NotNull(result.OffloadKey);
        Assert.Equal(blobAccessor.ProviderName, result.OffloadProvider);
    }

    [Fact]
    public async Task ProcessAsync_Should_Inline_At_Exact_Threshold_Boundary()
    {
        // Given: stream size equals threshold exactly
        const int threshold = 1000;
        var data = new byte[threshold];
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();

        // When
        var result = await StreamOffloadHelper.ProcessAsync(
            stream,
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
    public async Task ProcessAsync_Should_Offload_When_One_Byte_Over_Threshold()
    {
        // Given: stream size is threshold + 1
        const int threshold = 1000;
        var data = new byte[threshold + 1];
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();

        // When
        var result = await StreamOffloadHelper.ProcessAsync(
            stream,
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
    public async Task ProcessAsync_Should_Inline_When_BlobAccessor_Is_Null_Even_If_Above_Threshold()
    {
        // Given: no blob accessor available, data exceeds threshold
        var data = new byte[2000];
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);
        const int threshold = 1000;

        // When
        var result = await StreamOffloadHelper.ProcessAsync(
            stream,
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
    public async Task ProcessAsync_Should_Handle_Empty_Stream()
    {
        // Given: an empty stream
        using var stream = new MemoryStream(Array.Empty<byte>());
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();
        const int threshold = 1000;

        // When
        var result = await StreamOffloadHelper.ProcessAsync(
            stream,
            "test-projector/v1",
            threshold,
            blobAccessor,
            CancellationToken.None);

        // Then: empty data is inlined
        Assert.False(result.IsOffloaded);
        Assert.NotNull(result.InlineData);
        Assert.Empty(result.InlineData);
    }

    [Fact]
    public async Task ProcessAsync_Offloaded_Data_Should_Be_Retrievable_From_Blob()
    {
        // Given: data that will be offloaded
        var data = new byte[2000];
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();
        const int threshold = 1000;

        // When
        var result = await StreamOffloadHelper.ProcessAsync(
            stream,
            "test-projector/v1",
            threshold,
            blobAccessor,
            CancellationToken.None);

        // Then: offloaded data can be read back from blob
        Assert.True(result.IsOffloaded);
        Assert.NotNull(result.OffloadKey);
        await using var readStream = await blobAccessor.OpenReadAsync(result.OffloadKey);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);
        var readBack = ms.ToArray();
        Assert.Equal(data, readBack);
    }
}
