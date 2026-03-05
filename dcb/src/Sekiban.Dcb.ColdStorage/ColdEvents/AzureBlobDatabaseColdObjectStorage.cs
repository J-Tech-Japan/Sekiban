using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ResultBoxes;

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

    public async Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct)
        => await ExecuteAsync(path, ct, (inner, innerPath, innerCt) => inner.GetAsync(innerPath, innerCt));

    public async Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct)
        => await ExecuteAndPersistAsync(
            path,
            ct,
            (inner, innerPath, innerCt) => inner.PutAsync(innerPath, data, expectedETag, innerCt));

    public async Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct)
        => await ExecuteAsync(prefix, ct, (inner, innerPrefix, innerCt) => inner.ListAsync(innerPrefix, innerCt));

    public async Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct)
        => await ExecuteAndPersistAsync(path, ct, (inner, innerPath, innerCt) => inner.DeleteAsync(innerPath, innerCt));

    private async Task<ResultBox<T>> ExecuteAsync<T>(
        string input,
        CancellationToken ct,
        Func<IColdObjectStorage, string, CancellationToken, Task<ResultBox<T>>> action)
        where T : notnull
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var state = await DownloadDbAsync(ct);
            await using var cleanup = state;
            var inner = _createInnerStorage(state.LocalPath);
            return await action(inner, input, ct);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<T>(ex);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<ResultBox<bool>> ExecuteAndPersistAsync(
        string input,
        CancellationToken ct,
        Func<IColdObjectStorage, string, CancellationToken, Task<ResultBox<bool>>> action)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var state = await DownloadDbAsync(ct);
            await using var cleanup = state;
            var inner = _createInnerStorage(state.LocalPath);
            var result = await action(inner, input, ct);
            if (!result.IsSuccess || !result.GetValue())
            {
                return result;
            }

            await UploadDbAsync(state, ct);
            return result;
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            return ResultBox.Error<bool>(new InvalidOperationException(
                $"Concurrent update detected while writing {_storageKind} cold storage blob", ex));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
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
        var payload = await File.ReadAllBytesAsync(snapshot.LocalPath, ct);
        var conditions = new BlobRequestConditions();
        if (snapshot.Exists && snapshot.ETag.HasValue)
        {
            conditions.IfMatch = snapshot.ETag.Value;
        }
        else
        {
            conditions.IfNoneMatch = new ETag("*");
        }

        await blob.UploadAsync(
            BinaryData.FromBytes(payload),
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
