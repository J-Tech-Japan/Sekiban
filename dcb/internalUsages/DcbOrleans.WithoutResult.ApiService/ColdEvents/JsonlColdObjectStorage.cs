using ResultBoxes;
using Sekiban.Dcb.ColdEvents;

namespace DcbOrleans.WithoutResult.ApiService.ColdEvents;

public sealed class JsonlColdObjectStorage : IColdObjectStorage
{
    private readonly string _basePath;

    public JsonlColdObjectStorage(string basePath)
    {
        _basePath = Path.GetFullPath(basePath);
        Directory.CreateDirectory(_basePath);
    }

    public async Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct)
    {
        var fullPath = ColdStoragePath.ToAbsolute(_basePath, path);
        if (!File.Exists(fullPath))
        {
            return ResultBox.Error<ColdStorageObject>(new FileNotFoundException($"Cold object not found: {path}"));
        }

        try
        {
            var data = await File.ReadAllBytesAsync(fullPath, ct);
            return ResultBox.FromValue(new ColdStorageObject(data, ColdStoragePath.ComputeEtag(data)));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ColdStorageObject>(ex);
        }
    }

    public async Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct)
    {
        var fullPath = ColdStoragePath.ToAbsolute(_basePath, path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
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
                        return ResultBox.Error<bool>(new InvalidOperationException(
                            $"ETag mismatch at {path}: expected={expectedETag}, actual={etag}"));
                    }

                    stream.SetLength(0);
                    await stream.WriteAsync(data, ct);
                    await stream.FlushAsync(ct);
                    return ResultBox.FromValue(true);
                }
                catch (FileNotFoundException)
                {
                    return ResultBox.Error<bool>(new InvalidOperationException(
                        $"Conditional write failed: {path} does not exist"));
                }
            }

            // Create-if-not-exists first to reduce first-write races.
            try
            {
                await using var create = new FileStream(
                    fullPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.Asynchronous);
                await create.WriteAsync(data, ct);
                await create.FlushAsync(ct);
                return ResultBox.FromValue(true);
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
                await overwrite.WriteAsync(data, ct);
                await overwrite.FlushAsync(ct);
            }

            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    public Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct)
    {
        try
        {
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
                        return Task.FromResult(ResultBox.FromValue<IReadOnlyList<string>>([]));
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

            return Task.FromResult(ResultBox.FromValue<IReadOnlyList<string>>(result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ResultBox.Error<IReadOnlyList<string>>(ex));
        }
    }

    public Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct)
    {
        var fullPath = ColdStoragePath.ToAbsolute(_basePath, path);
        try
        {
            if (!File.Exists(fullPath))
            {
                return Task.FromResult(ResultBox.FromValue(false));
            }

            File.Delete(fullPath);
            return Task.FromResult(ResultBox.FromValue(true));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ResultBox.Error<bool>(ex));
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
