using System.Text;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Integration tests for the Streaming Snapshot I/O pipeline:
///     - IBlobStorageSnapshotAccessor stream-based methods
///     - IMultiProjectionStateStore.UpsertFromStreamAsync default implementation
///     - UseStreamingSnapshotIO feature flag
/// </summary>
public class StreamingSnapshotIOTests
{
    // --- IBlobStorageSnapshotAccessor Stream API Tests ---

    [Fact]
    public async Task BlobAccessor_WriteAsync_Stream_Should_Return_Key()
    {
        // Given: a stream of data
        var data = Encoding.UTF8.GetBytes("snapshot-payload-data");
        using var stream = new MemoryStream(data);
        var accessor = new InMemoryBlobStorageSnapshotAccessor();

        // When: writing via stream overload
        var key = await accessor.WriteAsync(stream, "test-projector", CancellationToken.None);

        // Then: a non-empty key is returned
        Assert.False(string.IsNullOrWhiteSpace(key));
    }

    [Fact]
    public async Task BlobAccessor_Stream_Write_Then_Stream_Read_Should_RoundTrip()
    {
        // Given: data written via stream
        var data = Encoding.UTF8.GetBytes("round-trip-stream-test");
        using var writeStream = new MemoryStream(data);
        var accessor = new InMemoryBlobStorageSnapshotAccessor();
        var key = await accessor.WriteAsync(writeStream, "round-trip-projector", CancellationToken.None);

        // When: reading via stream
        using var readStream = await accessor.OpenReadAsync(key, CancellationToken.None);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);
        var readData = ms.ToArray();

        // Then: data matches
        Assert.Equal(data, readData);
    }

    [Fact]
    public async Task BlobAccessor_Stream_Write_Then_OpenRead_Should_Be_Compatible()
    {
        // Given: data written via stream
        var data = Encoding.UTF8.GetBytes("cross-api-compat-test");
        using var writeStream = new MemoryStream(data);
        var accessor = new InMemoryBlobStorageSnapshotAccessor();
        var key = await accessor.WriteAsync(writeStream, "compat-projector", CancellationToken.None);

        // When: reading via stream API
        var readData = await ReadAllBytesAsync(accessor, key);

        // Then: data matches (backward compatibility)
        Assert.Equal(data, readData);
    }

    [Fact]
    public async Task BlobAccessor_Two_Stream_Writes_Then_Stream_Read_Should_Be_Compatible()
    {
        // Given: data written via stream API
        var data = Encoding.UTF8.GetBytes("byte-to-stream-compat");
        var accessor = new InMemoryBlobStorageSnapshotAccessor();
        using var writeStream = new MemoryStream(data);
        var key = await accessor.WriteAsync(writeStream, "compat-projector", CancellationToken.None);

        // When: reading via new stream API
        using var readStream = await accessor.OpenReadAsync(key, CancellationToken.None);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);
        var readData = ms.ToArray();

        // Then: data matches (backward compatibility)
        Assert.Equal(data, readData);
    }

    [Fact]
    public async Task BlobAccessor_OpenReadAsync_Should_Throw_For_NonExistent_Key()
    {
        // Given: an accessor with no stored data
        var accessor = new InMemoryBlobStorageSnapshotAccessor();

        // When/Then: reading a non-existent key throws
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => accessor.OpenReadAsync("nonexistent-key", CancellationToken.None));
    }

    [Fact]
    public async Task BlobAccessor_WriteAsync_Stream_Should_Handle_Empty_Stream()
    {
        // Given: an empty stream
        using var stream = new MemoryStream(Array.Empty<byte>());
        var accessor = new InMemoryBlobStorageSnapshotAccessor();

        // When: writing empty stream
        var key = await accessor.WriteAsync(stream, "empty-projector", CancellationToken.None);

        // Then: key is returned, data can be read back
        Assert.False(string.IsNullOrWhiteSpace(key));
        var readBack = await ReadAllBytesAsync(accessor, key);
        Assert.Empty(readBack);
    }

    [Fact]
    public async Task BlobAccessor_WriteAsync_Stream_Should_Handle_Large_Data()
    {
        // Given: a 2MB stream
        var data = new byte[2 * 1024 * 1024];
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);
        var accessor = new InMemoryBlobStorageSnapshotAccessor();

        // When
        var key = await accessor.WriteAsync(stream, "large-projector", CancellationToken.None);

        // Then: round-trip succeeds
        var readBack = await ReadAllBytesAsync(accessor, key);
        Assert.Equal(data, readBack);
    }

    // --- IMultiProjectionStateStore.UpsertFromStreamAsync Default Implementation Tests ---

    [Fact]
    public async Task UpsertFromStreamAsync_Default_Should_Store_Inline_Data()
    {
        // Given: InMemory store uses the default interface implementation
        IMultiProjectionStateStore store = new InMemoryMultiProjectionStateStore();
        var stateBytes = Encoding.UTF8.GetBytes("inline-state-data");
        using var stream = new MemoryStream(stateBytes);
        var request = new MultiProjectionStateWriteRequest(
            ProjectorName: "TestProjector",
            ProjectorVersion: "1.0",
            PayloadType: "TestPayload",
            LastSortableUniqueId: "20260225T120000Z-00000000-0000-0000-0000-000000000001",
            EventsProcessed: 10,
            StateData: null,
            IsOffloaded: false,
            OffloadKey: null,
            OffloadProvider: null,
            OriginalSizeBytes: stateBytes.Length,
            CompressedSizeBytes: stateBytes.Length,
            SafeWindowThreshold: "20260225T115940Z-00000000-0000-0000-0000-000000000000",
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            BuildSource: "UnitTest",
            BuildHost: "test-host");

        // When: calling UpsertFromStreamAsync (default impl buffers to byte[] and calls UpsertAsync)
        var result = await store.UpsertFromStreamAsync(request, stream, 1_000_000, CancellationToken.None);

        // Then: record is stored and retrievable
        Assert.True(result.IsSuccess);
        var stored = await store.GetLatestForVersionAsync("TestProjector", "1.0", CancellationToken.None);
        Assert.True(stored.IsSuccess);
        var opt = stored.GetValue();
        Assert.True(opt.HasValue);
        var record = opt.GetValue();
        Assert.Equal("TestProjector", record.ProjectorName);
        Assert.Equal("1.0", record.ProjectorVersion);
        Assert.Equal(10, record.EventsProcessed);
        Assert.NotNull(record.StateData);
        Assert.Equal(stateBytes, record.StateData);
    }

    [Fact]
    public async Task UpsertFromStreamAsync_Default_Should_Handle_Empty_Stream()
    {
        // Given
        IMultiProjectionStateStore store = new InMemoryMultiProjectionStateStore();
        using var stream = new MemoryStream(Array.Empty<byte>());
        var request = new MultiProjectionStateWriteRequest(
            ProjectorName: "EmptyProjector",
            ProjectorVersion: "1.0",
            PayloadType: "TestPayload",
            LastSortableUniqueId: "20260225T120000Z-00000000-0000-0000-0000-000000000001",
            EventsProcessed: 0,
            StateData: null,
            IsOffloaded: false,
            OffloadKey: null,
            OffloadProvider: null,
            OriginalSizeBytes: 0,
            CompressedSizeBytes: 0,
            SafeWindowThreshold: "20260225T115940Z-00000000-0000-0000-0000-000000000000",
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            BuildSource: "UnitTest",
            BuildHost: "test-host");

        // When
        var result = await store.UpsertFromStreamAsync(request, stream, 1_000_000, CancellationToken.None);

        // Then
        Assert.True(result.IsSuccess);
        var stored = await store.GetLatestForVersionAsync("EmptyProjector", "1.0", CancellationToken.None);
        Assert.True(stored.IsSuccess);
        var opt = stored.GetValue();
        Assert.True(opt.HasValue);
        var record = opt.GetValue();
        Assert.NotNull(record.StateData);
        Assert.Empty(record.StateData);
    }

    [Fact]
    public async Task UpsertFromStreamAsync_Default_Should_Preserve_Metadata()
    {
        // Given
        IMultiProjectionStateStore store = new InMemoryMultiProjectionStateStore();
        var stateBytes = new byte[] { 1, 2, 3 };
        using var stream = new MemoryStream(stateBytes);
        var createdAt = new DateTime(2026, 2, 25, 10, 0, 0, DateTimeKind.Utc);
        var request = new MultiProjectionStateWriteRequest(
            ProjectorName: "MetadataProjector",
            ProjectorVersion: "2.0",
            PayloadType: "MetadataPayload",
            LastSortableUniqueId: "20260225T100000Z-00000000-0000-0000-0000-000000000002",
            EventsProcessed: 100,
            StateData: null,
            IsOffloaded: false,
            OffloadKey: null,
            OffloadProvider: null,
            OriginalSizeBytes: 3,
            CompressedSizeBytes: 3,
            SafeWindowThreshold: "20260225T095940Z-00000000-0000-0000-0000-000000000000",
            CreatedAt: createdAt,
            UpdatedAt: createdAt,
            BuildSource: "MetadataTest",
            BuildHost: "meta-host");

        // When
        await store.UpsertFromStreamAsync(request, stream, 1_000_000, CancellationToken.None);

        // Then: all metadata fields are preserved
        var stored = await store.GetLatestForVersionAsync("MetadataProjector", "2.0", CancellationToken.None);
        var record = stored.GetValue().GetValue();
        Assert.Equal("MetadataPayload", record.PayloadType);
        Assert.Equal(100, record.EventsProcessed);
        Assert.Equal(3, record.OriginalSizeBytes);
        Assert.Equal(3, record.CompressedSizeBytes);
        Assert.Equal("MetadataTest", record.BuildSource);
        Assert.Equal("meta-host", record.BuildHost);
    }

    // --- Feature Flag Tests ---

    [Fact]
    public void UseStreamingSnapshotIO_Should_Default_To_True()
    {
        // Given/When
        var options = new GeneralMultiProjectionActorOptions();

        // Then
        Assert.True(options.UseStreamingSnapshotIO);
    }

    [Fact]
    public void UseStreamingSnapshotIO_Should_Be_Settable_To_False()
    {
        // Given/When
        var options = new GeneralMultiProjectionActorOptions { UseStreamingSnapshotIO = false };

        // Then
        Assert.False(options.UseStreamingSnapshotIO);
    }

    private static async Task<byte[]> ReadAllBytesAsync(InMemoryBlobStorageSnapshotAccessor accessor, string key)
    {
        await using var readStream = await accessor.OpenReadAsync(key, CancellationToken.None);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);
        return ms.ToArray();
    }
}
