namespace Sekiban.Dcb.ColdEvents.Tests;

/// <summary>
///     A missing object is <c>null</c> from <see cref="IColdObjectStorage.GetAsync" />, and a failure from
///     <see cref="IColdObjectStorage.OpenReadAsync" /> with the exception type each storage reported in 10.x.
/// </summary>
public sealed class ColdObjectStorageMissingObjectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cold-missing-{Guid.NewGuid():N}");

    public ColdObjectStorageMissingObjectTests() => Directory.CreateDirectory(_root);

    public static TheoryData<string> DatabaseStorages => new() { "sqlite", "duckdb" };

    private IColdObjectStorage Create(string kind) => kind switch
    {
        "sqlite" => new SqliteColdObjectStorage(Path.Combine(_root, "cold.sqlite")),
        "duckdb" => new DuckDbColdObjectStorage(Path.Combine(_root, "cold.duckdb")),
        _ => new JsonlColdObjectStorage(Path.Combine(_root, "jsonl"))
    };

    [Theory]
    [MemberData(nameof(DatabaseStorages))]
    public async Task DatabaseStorage_MissingObject_IsNull_AndOpenReadThrowsKeyNotFound(string kind)
    {
        var storage = Create(kind);

        Assert.Null(await storage.GetAsync("segments/missing.jsonl", CancellationToken.None));
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => storage.OpenReadAsync("segments/missing.jsonl", CancellationToken.None));
        Assert.Equal("Cold object not found: segments/missing.jsonl", ex.Message);
    }

    [Fact]
    public async Task JsonlStorage_MissingObject_IsNull_AndOpenReadThrowsFileNotFound()
    {
        var storage = Create("jsonl");

        Assert.Null(await storage.GetAsync("segments/missing.jsonl", CancellationToken.None));
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => storage.OpenReadAsync("segments/missing.jsonl", CancellationToken.None));
    }

    [Fact]
    public async Task LeaseVerification_SurfacesTheStoragesOwnMissingObjectException()
    {
        // The default OpenReadAsync reports a missing object as KeyNotFoundException.
        var keyNotFound = new StorageBackedColdLeaseManager(new VanishingWriteStorage());
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => keyNotFound.AcquireAsync("lease", TimeSpan.FromMinutes(1), CancellationToken.None));

        // A storage with its own OpenReadAsync (as JSONL) keeps its own exception type.
        var fileNotFound = new StorageBackedColdLeaseManager(new VanishingFileStorage());
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => fileNotFound.AcquireAsync("lease", TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A storage whose write succeeds but whose object is gone on the verification read.</summary>
    private class VanishingWriteStorage : IColdObjectStorage
    {
        public Task<ColdStorageObject?> GetAsync(string path, CancellationToken ct) =>
            Task.FromResult<ColdStorageObject?>(null);

        public Task<bool> PutAsync(string path, Stream data, string? expectedETag, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<bool> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<bool> DeleteAsync(string path, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class VanishingFileStorage : VanishingWriteStorage, IColdObjectStorage
    {
        public Task<Stream> OpenReadAsync(string path, CancellationToken ct) =>
            Task.FromException<Stream>(new FileNotFoundException($"Cold object not found: {path}"));
    }
}
