using System.Reflection;
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

    [Theory]
    [InlineData("jsonl", "jsonl")]
    [InlineData("sqlite", "cold-events.sqlite")]
    [InlineData("duckdb", "cold-events.duckdb")]
    public void Azure_blob_storage_should_scope_prefix_by_format(
        string format,
        string expectedScope)
    {
        var options = new ColdStorageOptions
        {
            Format = format,
            AzurePrefix = "MultiProjectionColdStorage"
        };

        var scopeMethod = typeof(ColdObjectStorageFactory).GetMethod("GetStorageScope", BindingFlags.Static | BindingFlags.NonPublic);
        var prefixMethod = typeof(ColdObjectStorageFactory).GetMethod("CombineAzurePrefix", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(scopeMethod);
        Assert.NotNull(prefixMethod);
        var scope = Assert.IsType<string>(scopeMethod!.Invoke(null, [options, format]));
        Assert.Equal(
            $"MultiProjectionColdStorage/{expectedScope}",
            Assert.IsType<string>(prefixMethod!.Invoke(null, [options.AzurePrefix, scope])));
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
