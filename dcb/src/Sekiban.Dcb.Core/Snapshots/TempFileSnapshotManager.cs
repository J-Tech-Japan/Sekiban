using Microsoft.Extensions.Logging;

namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Manages temporary file creation, deletion, and stale file cleanup
///     for the streaming snapshot persistence pipeline.
/// </summary>
public class TempFileSnapshotManager
{
    private const string FilePrefix = "snapshot-";
    private const string FileExtension = ".tmp";

    private readonly SnapshotTempFileOptions _options;
    private readonly ILogger<TempFileSnapshotManager> _logger;
    private readonly SemaphoreSlim _concurrencyGuard;

    public TempFileSnapshotManager(
        SnapshotTempFileOptions options,
        ILogger<TempFileSnapshotManager> logger)
    {
        _options = options;
        _logger = logger;
        _concurrencyGuard = new SemaphoreSlim(options.MaxConcurrentFiles, options.MaxConcurrentFiles);
    }

    /// <summary>
    ///     Creates a temp file and returns a writable, seekable FileStream with the file path.
    ///     The directory is created automatically if it does not exist.
    /// </summary>
    public async Task<(FileStream Stream, string FilePath)> CreateTempFileStreamAsync(string projectorName)
    {
        await _concurrencyGuard.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_options.TempDirectory);

            // Total size guard: reject new temp files when existing files exceed the limit
            var totalSize = GetExistingTempFilesTotalSize();
            if (totalSize >= _options.MaxTotalSizeBytes)
            {
                throw new InvalidOperationException(
                    $"Temp file total size ({totalSize} bytes) exceeds limit ({_options.MaxTotalSizeBytes} bytes). " +
                    "Consider running cleanup or increasing MaxTotalSizeBytes.");
            }

            var safeProjectorName = SanitizeFileNameSegment(projectorName);
            var fileName = $"{FilePrefix}{safeProjectorName}-{Guid.NewGuid():N}{FileExtension}";
            var filePath = Path.Combine(_options.TempDirectory, fileName);

            var stream = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous);

            return (stream, filePath);
        }
        catch
        {
            _concurrencyGuard.Release();
            throw;
        }
    }

    /// <summary>
    ///     Safely deletes a temp file. Logs a warning on failure but never throws.
    /// </summary>
    public Task SafeDeleteAsync(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp snapshot file: {FilePath}", filePath);
        }

        // Release the concurrency guard slot
        try
        {
            _concurrencyGuard.Release();
        }
        catch (SemaphoreFullException)
        {
            // Guard was already released (e.g. double-delete call)
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Removes temp files older than <see cref="SnapshotTempFileOptions.StaleFileTimeoutMinutes" />.
    ///     Does not throw if the directory does not exist.
    /// </summary>
    public Task CleanupStaleFilesAsync()
    {
        if (!Directory.Exists(_options.TempDirectory))
        {
            return Task.CompletedTask;
        }

        var cutoff = DateTime.UtcNow.AddMinutes(-_options.StaleFileTimeoutMinutes);

        try
        {
            foreach (var file in Directory.EnumerateFiles(_options.TempDirectory, $"{FilePrefix}*{FileExtension}"))
            {
                try
                {
                    var lastWrite = File.GetLastWriteTimeUtc(file);
                    if (lastWrite < cutoff)
                    {
                        File.Delete(file);
                        _logger.LogDebug("Cleaned up stale temp file: {FilePath}", file);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up stale temp file: {FilePath}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during stale file cleanup in: {TempDirectory}", _options.TempDirectory);
        }

        return Task.CompletedTask;
    }

    // Path.GetInvalidFileNameChars() on Linux/macOS only returns '\0' and '/'.
    // We must explicitly include characters that are invalid on Windows (e.g. ':')
    // to ensure cross-platform compatibility.
    private static readonly HashSet<char> InvalidFileNameChars =
        new(Path.GetInvalidFileNameChars().Concat(new[] { ':', '<', '>', '|', '"', '?', '*' }));

    private static string SanitizeFileNameSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (InvalidFileNameChars.Contains(chars[i]))
            {
                chars[i] = '_';
            }
        }
        return new string(chars);
    }

    private long GetExistingTempFilesTotalSize()
    {
        if (!Directory.Exists(_options.TempDirectory))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(_options.TempDirectory, $"{FilePrefix}*{FileExtension}"))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (IOException)
            {
                // File may have been deleted between enumeration and size query (TOCTOU)
            }
        }
        return total;
    }
}
