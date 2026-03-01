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
                if (!File.Exists(fullPath))
                {
                    return ResultBox.Error<bool>(new InvalidOperationException($"Conditional write failed: {path} does not exist"));
                }

                var existing = await File.ReadAllBytesAsync(fullPath, ct);
                var etag = ColdStoragePath.ComputeEtag(existing);
                if (!string.Equals(etag, expectedETag, StringComparison.Ordinal))
                {
                    return ResultBox.Error<bool>(new InvalidOperationException($"ETag mismatch at {path}: expected={expectedETag}, actual={etag}"));
                }
            }

            await File.WriteAllBytesAsync(fullPath, data, ct);
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
            var result = Directory
                .EnumerateFiles(_basePath, "*", SearchOption.AllDirectories)
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
}
