namespace Sekiban.Dcb.ColdEvents;

public sealed class JsonlColdObjectStorage : IColdObjectStorage
{
    private readonly string _basePath;

    public JsonlColdObjectStorage(string basePath)
    {
        _basePath = Path.GetFullPath(basePath);
        Directory.CreateDirectory(_basePath);
    }

    public async Task<ColdStorageObject?> GetAsync(string path, CancellationToken ct)
    {
        var fullPath = ColdStoragePath.ToAbsolute(_basePath, path);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        byte[] data;
        try
        {
            data = await File.ReadAllBytesAsync(fullPath, ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Removed between the existence check and the read: still "not found".
            return null;
        }

        return new ColdStorageObject(data, ColdStoragePath.ComputeEtag(data));
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fullPath = ColdStoragePath.ToAbsolute(_basePath, path);
        if (!File.Exists(fullPath))
        {
            return Task.FromException<Stream>(new FileNotFoundException($"Cold object not found: {path}"));
        }

        try
        {
            Stream stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(stream);
        }
        catch (Exception ex)
        {
            return Task.FromException<Stream>(ex);
        }
    }

    public async Task<bool> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct)
    {
        await using var stream = new MemoryStream(data, writable: false);
        return await PutAsync(path, stream, expectedETag, ct);
    }

    public async Task<bool> PutAsync(string path, Stream data, string? expectedETag, CancellationToken ct)
    {
        var fullPath = ColdStoragePath.ToAbsolute(_basePath, path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (expectedETag is not null)
        {
            try
            {
                await using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.Asynchronous);

                var existing = await ReadAllAsync(stream, ct);
                var etag = ColdStoragePath.ComputeEtag(existing);
                if (!string.Equals(etag, expectedETag, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"ETag mismatch at {path}: expected={expectedETag}, actual={etag}");
                }

                stream.SetLength(0);
                if (data.CanSeek)
                {
                    data.Position = 0;
                }

                await data.CopyToAsync(stream, ct);
                await stream.FlushAsync(ct);
                return true;
            }
            catch (FileNotFoundException)
            {
                throw new InvalidOperationException(
                    $"Conditional write failed: {path} does not exist");
            }
        }

        try
        {
            await using var create = new FileStream(
                fullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous);
            if (data.CanSeek)
            {
                data.Position = 0;
            }

            await data.CopyToAsync(create, ct);
            await create.FlushAsync(ct);
            return true;
        }
        catch (IOException) when (File.Exists(fullPath))
        {
            await using var overwrite = new FileStream(
                fullPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous);
            if (data.CanSeek)
            {
                data.Position = 0;
            }

            await data.CopyToAsync(overwrite, ct);
            await overwrite.FlushAsync(ct);
        }

        return true;
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var normalizedPrefix = ColdStoragePath.Normalize(prefix);
            var searchRoot = _basePath;
            if (!string.IsNullOrWhiteSpace(normalizedPrefix))
            {
                var firstSeparator = normalizedPrefix.IndexOf('/');
                var firstSegment = firstSeparator >= 0
                    ? normalizedPrefix[..firstSeparator]
                    : normalizedPrefix;
                if (!string.IsNullOrWhiteSpace(firstSegment))
                {
                    var candidate = Path.Combine(_basePath, firstSegment);
                    if (!Directory.Exists(candidate))
                    {
                        return Task.FromResult<IReadOnlyList<string>>([]);
                    }

                    searchRoot = candidate;
                }
            }

            var result = Directory
                .EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(_basePath, file).Replace('\\', '/'))
                .Where(relative => relative.StartsWith(normalizedPrefix, StringComparison.Ordinal))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult<IReadOnlyList<string>>(result);
        }
        catch (Exception ex)
        {
            return Task.FromException<IReadOnlyList<string>>(ex);
        }
    }

    public Task<bool> DeleteAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fullPath = ColdStoragePath.ToAbsolute(_basePath, path);
        try
        {
            if (!File.Exists(fullPath))
            {
                return Task.FromResult(false);
            }

            File.Delete(fullPath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            return Task.FromException<bool>(ex);
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken ct)
    {
        stream.Position = 0;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
}
