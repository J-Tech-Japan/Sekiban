using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Sekiban.Dcb.ColdEvents;

public sealed class AzureBlobDatabaseColdObjectStorage : IColdObjectStorage
{
    private readonly BlobContainerClient _container;
    private readonly string _blobName;
    private readonly Func<string, IColdObjectStorage> _createInnerStorage;
    private readonly string _storageKind;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public AzureBlobDatabaseColdObjectStorage(
        BlobServiceClient blobServiceClient,
        string containerName,
        string? prefix,
        string databaseFileName,
        Func<string, IColdObjectStorage> createInnerStorage,
        string storageKind)
    {
        _container = blobServiceClient.GetBlobContainerClient(containerName);
        _blobName = BuildBlobName(prefix, databaseFileName);
        _createInnerStorage = createInnerStorage;
        _storageKind = storageKind;
    }

    public Task<ColdStorageObject?> GetAsync(string path, CancellationToken ct)
        => ExecuteAsync(path, ct, (inner, innerPath, innerCt) => inner.GetAsync(innerPath, innerCt));

    public Task<bool> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct)
        => ExecuteAndPersistAsync(
            path,
            ct,
            (inner, innerPath, innerCt) => inner.PutAsync(innerPath, data, expectedETag, innerCt));

    public Task<bool> PutAsync(string path, Stream data, string? expectedETag, CancellationToken ct)
        => ExecuteAndPersistAsync(
            path,
            ct,
            (inner, innerPath, innerCt) => inner.PutAsync(innerPath, data, expectedETag, innerCt));

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct)
        => ExecuteAsync(prefix, ct, (inner, innerPrefix, innerCt) => inner.ListAsync(innerPrefix, innerCt));

    public Task<bool> DeleteAsync(string path, CancellationToken ct)
        => ExecuteAndPersistAsync(path, ct, (inner, innerPath, innerCt) => inner.DeleteAsync(innerPath, innerCt));

    private async Task<T> ExecuteAsync<T>(
        string input,
        CancellationToken ct,
        Func<IColdObjectStorage, string, CancellationToken, Task<T>> action)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var state = await DownloadDbAsync(ct);
            await using var cleanup = state;
            var inner = _createInnerStorage(state.LocalPath);
            return await action(inner, input, ct);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<bool> ExecuteAndPersistAsync(
        string input,
        CancellationToken ct,
        Func<IColdObjectStorage, string, CancellationToken, Task<bool>> action)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var state = await DownloadDbAsync(ct);
            await using var cleanup = state;
            var inner = _createInnerStorage(state.LocalPath);
            if (!await action(inner, input, ct))
            {
                return false;
            }

            await UploadDbAsync(state, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            throw new InvalidOperationException(
                $"Concurrent update detected while writing {_storageKind} cold storage blob", ex);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<DbSnapshot> DownloadDbAsync(CancellationToken ct)
    {
        var localPath = Path.Combine(Path.GetTempPath(), $"sekiban-cold-{_storageKind}-{Guid.NewGuid():N}.db");
        var blob = _container.GetBlobClient(_blobName);

        try
        {
            var props = await blob.GetPropertiesAsync(cancellationToken: ct);
            await blob.DownloadToAsync(localPath, cancellationToken: ct);
            return new DbSnapshot(localPath, true, props.Value.ETag);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new DbSnapshot(localPath, false, null);
        }
    }

    private async Task UploadDbAsync(DbSnapshot snapshot, CancellationToken ct)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);

        var blob = _container.GetBlobClient(_blobName);
        var conditions = new BlobRequestConditions();
        if (snapshot.Exists && snapshot.ETag.HasValue)
        {
            conditions.IfMatch = snapshot.ETag.Value;
        }
        else
        {
            conditions.IfNoneMatch = new ETag("*");
        }

        await using var payload = new FileStream(
            snapshot.LocalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await blob.UploadAsync(
            payload,
            new BlobUploadOptions { Conditions = conditions },
            ct);
    }

    private static string BuildBlobName(string? prefix, string fileName)
    {
        var normalizedFileName = fileName.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return normalizedFileName;
        }

        return $"{prefix.TrimEnd('/')}/{normalizedFileName}";
    }

    private sealed class DbSnapshot : IAsyncDisposable
    {
        public DbSnapshot(string localPath, bool exists, ETag? etag)
        {
            LocalPath = localPath;
            Exists = exists;
            ETag = etag;
        }

        public string LocalPath { get; }
        public bool Exists { get; }
        public ETag? ETag { get; }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (File.Exists(LocalPath))
                {
                    File.Delete(LocalPath);
                }
            }
            catch
            {
                // Best effort cleanup.
            }

            return ValueTask.CompletedTask;
        }
    }
}
