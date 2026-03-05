using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ResultBoxes;

namespace Sekiban.Dcb.ColdEvents;

public sealed class AzureBlobSqliteColdObjectStorage : IColdObjectStorage
{
    private readonly BlobContainerClient _container;
    private readonly string _blobName;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public AzureBlobSqliteColdObjectStorage(
        BlobServiceClient blobServiceClient,
        string containerName,
        string? prefix,
        string databaseFileName)
    {
        _container = blobServiceClient.GetBlobContainerClient(containerName);
        _blobName = BuildBlobName(prefix, databaseFileName);
    }

    public async Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var state = await DownloadDbAsync(ct);
            await using var cleanup = state;
            var inner = new SqliteColdObjectStorage(state.LocalPath);
            return await inner.GetAsync(path, ct);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ColdStorageObject>(ex);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var state = await DownloadDbAsync(ct);
            await using var cleanup = state;
            var inner = new SqliteColdObjectStorage(state.LocalPath);
            var put = await inner.PutAsync(path, data, expectedETag, ct);
            if (!put.IsSuccess)
            {
                return put;
            }

            await UploadDbAsync(state, ct);
            return put;
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            return ResultBox.Error<bool>(new InvalidOperationException(
                "Concurrent update detected while writing sqlite cold storage blob", ex));
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

    public async Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var state = await DownloadDbAsync(ct);
            await using var cleanup = state;
            var inner = new SqliteColdObjectStorage(state.LocalPath);
            return await inner.ListAsync(prefix, ct);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IReadOnlyList<string>>(ex);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var state = await DownloadDbAsync(ct);
            await using var cleanup = state;
            var inner = new SqliteColdObjectStorage(state.LocalPath);
            var deleted = await inner.DeleteAsync(path, ct);
            if (!deleted.IsSuccess)
            {
                return deleted;
            }

            if (!deleted.GetValue())
            {
                return deleted;
            }

            await UploadDbAsync(state, ct);
            return deleted;
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            return ResultBox.Error<bool>(new InvalidOperationException(
                "Concurrent update detected while deleting from sqlite cold storage blob", ex));
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
        var localPath = Path.Combine(Path.GetTempPath(), $"sekiban-cold-sqlite-{Guid.NewGuid():N}.db");
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
