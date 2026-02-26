using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Unit tests for TempFileSnapshotManager — manages temp file creation,
///     deletion, and stale file cleanup for streaming snapshot persistence.
/// </summary>
public class TempFileSnapshotManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotTempFileOptions _options;
    private readonly TempFileSnapshotManager _manager;

    public TempFileSnapshotManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sekiban-test-{Guid.NewGuid():N}");
        _options = new SnapshotTempFileOptions
        {
            TempDirectory = _tempDir,
            MaxConcurrentFiles = 3,
            MaxTotalSizeBytes = 10 * 1024 * 1024,
            StaleFileTimeoutMinutes = 30
        };
        _manager = new TempFileSnapshotManager(
            _options,
            NullLogger<TempFileSnapshotManager>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // --- SnapshotTempFileOptions defaults ---

    [Fact]
    public void Options_Should_Have_Expected_Defaults()
    {
        // When
        var defaults = new SnapshotTempFileOptions();

        // Then
        var expectedDir = Path.Combine(Path.GetTempPath(), "sekiban-snapshots");
        Assert.Equal(expectedDir, defaults.TempDirectory);
        Assert.Equal(10, defaults.MaxConcurrentFiles);
        Assert.Equal(500L * 1024 * 1024, defaults.MaxTotalSizeBytes);
        Assert.Equal(30, defaults.StaleFileTimeoutMinutes);
    }

    // --- CreateTempFileStreamAsync ---

    [Fact]
    public async Task CreateTempFileStreamAsync_Should_Create_File_In_Configured_Directory()
    {
        // When
        var (stream, filePath) = await _manager.CreateTempFileStreamAsync("TestProjector");

        // Then
        try
        {
            Assert.True(File.Exists(filePath));
            Assert.StartsWith(_tempDir, filePath);
            Assert.True(stream.CanWrite);
        }
        finally
        {
            await stream.DisposeAsync();
            await _manager.SafeDeleteAsync(filePath);
        }
    }

    [Fact]
    public async Task CreateTempFileStreamAsync_Should_Create_Directory_If_Not_Exists()
    {
        // Given: directory does not exist yet
        Assert.False(Directory.Exists(_tempDir));

        // When
        var (stream, filePath) = await _manager.CreateTempFileStreamAsync("TestProjector");

        // Then
        try
        {
            Assert.True(Directory.Exists(_tempDir));
            Assert.True(File.Exists(filePath));
        }
        finally
        {
            await stream.DisposeAsync();
            await _manager.SafeDeleteAsync(filePath);
        }
    }

    [Fact]
    public async Task CreateTempFileStreamAsync_Should_Use_Expected_FileName_Pattern()
    {
        // When
        var (stream, filePath) = await _manager.CreateTempFileStreamAsync("MyProjector");

        // Then
        try
        {
            var fileName = Path.GetFileName(filePath);
            Assert.StartsWith("snapshot-MyProjector-", fileName);
            Assert.EndsWith(".tmp", fileName);
        }
        finally
        {
            await stream.DisposeAsync();
            await _manager.SafeDeleteAsync(filePath);
        }
    }

    [Fact]
    public async Task CreateTempFileStreamAsync_Should_Sanitize_ProjectorName_For_FileSystem()
    {
        // When: projector name contains path separator and reserved characters
        var (stream, filePath) = await _manager.CreateTempFileStreamAsync("svc/default:Projector");

        // Then: generated file name is safe and does not contain directory separators
        try
        {
            var fileName = Path.GetFileName(filePath);
            Assert.DoesNotContain("/", fileName);
            Assert.DoesNotContain(":", fileName);
            Assert.Contains("svc_default_Projector", fileName);
            Assert.True(File.Exists(filePath));
        }
        finally
        {
            await stream.DisposeAsync();
            await _manager.SafeDeleteAsync(filePath);
        }
    }

    [Fact]
    public async Task CreateTempFileStreamAsync_Should_Return_Writable_Seekable_FileStream()
    {
        // When
        var (stream, filePath) = await _manager.CreateTempFileStreamAsync("TestProjector");

        // Then
        try
        {
            Assert.IsType<FileStream>(stream);
            Assert.True(stream.CanWrite);
            Assert.True(stream.CanSeek);

            // Verify writing works
            var data = new byte[] { 1, 2, 3, 4, 5 };
            await stream.WriteAsync(data);
            Assert.Equal(5, stream.Length);
        }
        finally
        {
            await stream.DisposeAsync();
            await _manager.SafeDeleteAsync(filePath);
        }
    }

    [Fact]
    public async Task CreateTempFileStreamAsync_Should_Generate_Unique_Paths()
    {
        // When: creating multiple temp files for the same projector
        var (stream1, path1) = await _manager.CreateTempFileStreamAsync("TestProjector");
        var (stream2, path2) = await _manager.CreateTempFileStreamAsync("TestProjector");

        // Then: paths are unique
        try
        {
            Assert.NotEqual(path1, path2);
        }
        finally
        {
            await stream1.DisposeAsync();
            await stream2.DisposeAsync();
            await _manager.SafeDeleteAsync(path1);
            await _manager.SafeDeleteAsync(path2);
        }
    }

    // --- SafeDeleteAsync ---

    [Fact]
    public async Task SafeDeleteAsync_Should_Delete_File()
    {
        // Given: a temp file exists
        var (stream, filePath) = await _manager.CreateTempFileStreamAsync("TestProjector");
        await stream.DisposeAsync();
        Assert.True(File.Exists(filePath));

        // When
        await _manager.SafeDeleteAsync(filePath);

        // Then
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task SafeDeleteAsync_Should_Not_Throw_For_NonExistent_File()
    {
        // Given: a file path that does not exist
        var fakePath = Path.Combine(_tempDir, "nonexistent.tmp");

        // When/Then: no exception thrown
        await _manager.SafeDeleteAsync(fakePath);
    }

    // --- CleanupStaleFilesAsync ---

    [Fact]
    public async Task CleanupStaleFilesAsync_Should_Remove_Old_Files()
    {
        // Given: a stale temp file with old LastWriteTime
        Directory.CreateDirectory(_tempDir);
        var stalePath = Path.Combine(_tempDir, "snapshot-stale-old.tmp");
        await File.WriteAllBytesAsync(stalePath, new byte[] { 1, 2, 3 });
        File.SetLastWriteTimeUtc(stalePath, DateTime.UtcNow.AddMinutes(-60));

        // When
        await _manager.CleanupStaleFilesAsync();

        // Then: stale file removed
        Assert.False(File.Exists(stalePath));
    }

    [Fact]
    public async Task CleanupStaleFilesAsync_Should_Keep_Recent_Files()
    {
        // Given: a recently created temp file
        Directory.CreateDirectory(_tempDir);
        var recentPath = Path.Combine(_tempDir, "snapshot-recent-new.tmp");
        await File.WriteAllBytesAsync(recentPath, new byte[] { 1, 2, 3 });
        File.SetLastWriteTimeUtc(recentPath, DateTime.UtcNow);

        // When
        await _manager.CleanupStaleFilesAsync();

        // Then: recent file kept
        Assert.True(File.Exists(recentPath));

        // Cleanup
        File.Delete(recentPath);
    }

    [Fact]
    public async Task CleanupStaleFilesAsync_Should_Not_Throw_When_Directory_Missing()
    {
        // Given: a manager whose temp directory does not exist
        var missingDir = Path.Combine(Path.GetTempPath(), $"sekiban-missing-{Guid.NewGuid():N}");
        var opts = new SnapshotTempFileOptions { TempDirectory = missingDir };
        var mgr = new TempFileSnapshotManager(
            opts,
            NullLogger<TempFileSnapshotManager>.Instance);

        // When/Then: no exception thrown
        await mgr.CleanupStaleFilesAsync();
    }

    // --- MaxTotalSizeBytes guard ---

    [Fact]
    public async Task CreateTempFileStreamAsync_Should_Throw_When_TotalSize_Exceeds_Limit()
    {
        // Given: a manager with a small MaxTotalSizeBytes limit
        var guardDir = Path.Combine(Path.GetTempPath(), $"sekiban-guard-{Guid.NewGuid():N}");
        var guardOpts = new SnapshotTempFileOptions
        {
            TempDirectory = guardDir,
            MaxConcurrentFiles = 10,
            MaxTotalSizeBytes = 100,
            StaleFileTimeoutMinutes = 30
        };
        var guardManager = new TempFileSnapshotManager(
            guardOpts,
            NullLogger<TempFileSnapshotManager>.Instance);

        try
        {
            // Place an existing file that exceeds the 100-byte limit
            Directory.CreateDirectory(guardDir);
            var existingFile = Path.Combine(guardDir, "snapshot-existing-filler.tmp");
            await File.WriteAllBytesAsync(existingFile, new byte[150]);

            // When/Then: creating a new temp file should throw InvalidOperationException
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => guardManager.CreateTempFileStreamAsync("TestProjector"));
            Assert.Contains("exceeds limit", ex.Message);
        }
        finally
        {
            if (Directory.Exists(guardDir))
            {
                Directory.Delete(guardDir, recursive: true);
            }
        }
    }
}
