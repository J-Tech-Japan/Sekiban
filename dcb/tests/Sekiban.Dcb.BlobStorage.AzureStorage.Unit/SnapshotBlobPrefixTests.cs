using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Sekiban.Dcb.Snapshots;
using Xunit;

namespace Sekiban.Dcb.BlobStorage.AzureStorage.Unit;

[Collection("AzuriteCollection")]
public class SnapshotBlobPrefixTests(AzuriteTestFixture fixture) : IAsyncLifetime
{
    private readonly string _containerName = $"prefix-test-{Guid.NewGuid():N}";
    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), $"prefix-test-{Guid.NewGuid():N}");
    private BlobContainerClient Container => new(fixture.ConnectionString, _containerName);
    private AzureBlobStorageSnapshotAccessor Accessor(string? prefix = null) =>
        new(fixture.ConnectionString, _containerName, prefix, _cacheDirectory);
    private static byte[] Payload => Encoding.UTF8.GetBytes("snapshot-prefix-regression");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await Container.DeleteIfExistsAsync();
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SeekableWrite_LandsUnderPrefix()
    {
        using var stream = new MemoryStream(Payload);
        var hash = await StreamOffloadHelper.ComputeContentHashAsync(stream, CancellationToken.None);
        var key = await Accessor("tenant-a///").WriteAsync(stream, "projector/v1");

        Assert.Equal($"tenant-a/projector/v1/{hash}.bin", key);
        Assert.True((await Container.GetBlobClient(key).ExistsAsync()).Value);
        var names = new List<string>();
        await foreach (var blob in Container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: "tenant-a/", cancellationToken: CancellationToken.None))
        {
            names.Add(blob.Name);
        }
        Assert.Equal(new[] { key }, names);
    }

    [Fact]
    public async Task DifferentPrefixes_StoreIdenticalContentAtDifferentKeys()
    {
        using var first = new MemoryStream(Payload);
        using var second = new MemoryStream(Payload);
        var firstKey = await Accessor("tenant-a").WriteAsync(first, "projector");
        var secondKey = await Accessor("tenant-b").WriteAsync(second, "projector");

        Assert.NotEqual(firstKey, secondKey);
        Assert.True((await Container.GetBlobClient(firstKey).ExistsAsync()).Value);
        Assert.True((await Container.GetBlobClient(secondKey).ExistsAsync()).Value);
        Assert.Equal(2, (await BlobNamesAsync()).Count);
    }

    [Fact]
    public async Task PrefixedAccessor_ReadsPreChangeUnprefixedBlobFromStorage()
    {
        using var source = new MemoryStream(Payload);
        var hash = await StreamOffloadHelper.ComputeContentHashAsync(source, CancellationToken.None);
        var oldKey = StreamOffloadHelper.ComputeDeterministicKey("projector/v1", hash);
        await Container.CreateAsync();
        await Container.GetBlobClient(oldKey).UploadAsync(source);
        Assert.False(Directory.Exists(_cacheDirectory));

        await using var read = await Accessor("tenant-a").OpenReadAsync(oldKey);
        using var result = new MemoryStream();
        await read.CopyToAsync(result);
        Assert.Equal(Payload, result.ToArray());
        Assert.True(Directory.Exists(_cacheDirectory));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task NoPrefix_PreservesExistingKey(string? prefix)
    {
        using var stream = new MemoryStream(Payload);
        var hash = await StreamOffloadHelper.ComputeContentHashAsync(stream, CancellationToken.None);
        var key = await Accessor(prefix).WriteAsync(stream, "projector/v1");
        Assert.Equal(StreamOffloadHelper.ComputeDeterministicKey("projector/v1", hash), key);
        Assert.True((await Container.GetBlobClient(key).ExistsAsync()).Value);
    }

    [Fact]
    public async Task PrefixedKey_Exactly512Characters_IsWritten()
    {
        using var stream = new MemoryStream(Payload);
        var key = await Accessor(new string('t', 57)).WriteAsync(stream,
            $"{new string('p', 256)}/{new string('v', 128)}");
        Assert.Equal(512, key.Length);
        Assert.True((await Container.GetBlobClient(key).ExistsAsync()).Value);
    }

    [Fact]
    public async Task PrefixedKey_513Characters_ThrowsBeforeUploading()
    {
        using var stream = new MemoryStream(Payload);
        var prefix = new string('t', 58);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Accessor(prefix).WriteAsync(stream,
            $"{new string('p', 256)}/{new string('v', 128)}"));
        Assert.Contains(prefix, error.Message);
        Assert.Contains("513", error.Message);
        Assert.Contains("512", error.Message);
        Assert.Empty(await BlobNamesAsync());
    }

    [Fact]
    public async Task NoPrefix_LongKey_IsStillWritten()
    {
        using var stream = new MemoryStream(Payload);
        var projector = new string('p', 445);
        var hash = await StreamOffloadHelper.ComputeContentHashAsync(stream, CancellationToken.None);
        var key = await Accessor().WriteAsync(stream, projector);
        Assert.Equal(StreamOffloadHelper.ComputeDeterministicKey(projector, hash), key);
        Assert.True(key.Length > 512);
        Assert.True((await Container.GetBlobClient(key).ExistsAsync()).Value);
    }

    [Fact]
    public async Task NonSeekableWrite_KeepsPrefixedGuidKey()
    {
        using var stream = new NonSeekableStream(Payload);
        var key = await Accessor("tenant-a///").WriteAsync(stream, "projector/v1");
        Assert.StartsWith("tenant-a/projector/v1/", key);
        Assert.EndsWith(".bin", key);
        Assert.True(Guid.TryParseExact(key["tenant-a/projector/v1/".Length..^4], "N", out _));
        var downloaded = await Container.GetBlobClient(key).DownloadContentAsync();
        Assert.Equal(Payload, downloaded.Value.Content.ToArray());
    }

    private async Task<List<string>> BlobNamesAsync()
    {
        var names = new List<string>();
        await foreach (var blob in Container.GetBlobsAsync())
        {
            names.Add(blob.Name);
        }
        return names;
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
