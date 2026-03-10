using System.Text;

namespace Sekiban.Dcb.ColdEvents.Tests;

public sealed class ColdObjectStorageFactoryTests
{
    [Theory]
    [InlineData("sqlite", "legacy-sqlite.db")]
    [InlineData("duckdb", "legacy-duckdb.db")]
    public async Task Create_should_scope_segment_storage_using_legacy_database_name(
        string format,
        string fileName)
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), $"sekiban-cold-storage-{Guid.NewGuid():N}");

        try
        {
            var options = new ColdStorageOptions
            {
                Format = format,
                SqliteFile = format == "sqlite" ? fileName : "cold-events.sqlite",
                DuckDbFile = format == "duckdb" ? fileName : "cold-events.duckdb"
            };

            var storage = ColdObjectStorageFactory.Create(
                options,
                storageRoot,
                new NullServiceProvider());

            var writeResult = await storage.PutAsync(
                "control/default/manifest.json",
                Encoding.UTF8.GetBytes("{}"),
                expectedETag: null,
                CancellationToken.None);

            Assert.True(writeResult.IsSuccess);
            Assert.True(
                File.Exists(Path.Combine(storageRoot, fileName, "control", "default", "manifest.json")));
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("sqlite", "legacy-sqlite.db")]
    [InlineData("duckdb", "legacy-duckdb.db")]
    public void Create_should_fail_with_guidance_when_legacy_database_file_blocks_scoped_storage(
        string format,
        string fileName)
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), $"sekiban-cold-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        var legacyFile = Path.Combine(storageRoot, fileName);
        File.WriteAllBytes(legacyFile, [1, 2, 3]);

        try
        {
            var options = new ColdStorageOptions
            {
                Format = format,
                SqliteFile = format == "sqlite" ? fileName : "cold-events.sqlite",
                DuckDbFile = format == "duckdb" ? fileName : "cold-events.duckdb"
            };

            var ex = Assert.Throws<InvalidOperationException>(() => ColdObjectStorageFactory.Create(
                options,
                storageRoot,
                new NullServiceProvider()));

            Assert.Contains("directory scope", ex.Message);
            Assert.Contains(legacyFile, ex.Message);
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Azure_blob_storage_should_preserve_legacy_jsonl_prefix_by_default()
    {
        var options = new ColdStorageOptions
        {
            Format = "jsonl",
            AzurePrefix = "MultiProjectionColdStorage"
        };

        Assert.Equal(
            "MultiProjectionColdStorage",
            ColdObjectStorageFactory.GetAzureStoragePrefix(options, "jsonl"));
    }

    [Fact]
    public void Azure_blob_storage_should_scope_custom_jsonl_prefix()
    {
        var options = new ColdStorageOptions
        {
            Format = "jsonl",
            AzurePrefix = "MultiProjectionColdStorage",
            JsonlDirectory = "JSONL"
        };

        Assert.Equal(
            "MultiProjectionColdStorage/JSONL",
            ColdObjectStorageFactory.GetAzureStoragePrefix(options, "jsonl"));
    }

    [Theory]
    [InlineData("sqlite", "cold-events.sqlite")]
    [InlineData("duckdb", "cold-events.duckdb")]
    public void Azure_blob_storage_should_scope_segment_formats_using_configured_scope(
        string format,
        string scope)
    {
        var options = new ColdStorageOptions
        {
            Format = format,
            AzurePrefix = "MultiProjectionColdStorage"
        };

        Assert.Equal(
            $"MultiProjectionColdStorage/{scope}",
            ColdObjectStorageFactory.GetAzureStoragePrefix(options, format));
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
