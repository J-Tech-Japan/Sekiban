namespace Sekiban.Dcb.ColdEvents;

public sealed record ColdStorageOptions
{
    // Legacy single-field selector. Supported values:
    // jsonl/sqlite/duckdb/azureblob
    public string Type { get; init; } = "jsonl";

    // Preferred selector for storage location. Supported values:
    // local/azureblob
    public string? Provider { get; init; }

    // Preferred selector for storage format. Supported values:
    // jsonl/sqlite/duckdb
    public string? Format { get; init; }

    // Relative paths are resolved against the application's current directory.
    public string BasePath { get; init; } = ColdObjectStorageFactory.DefaultBasePath;

    public string JsonlDirectory { get; init; } = "JSONL";

    // For segmented sqlite storage, this becomes the artifact scope directory/prefix.
    public string SqliteFile { get; init; } = "SQLite";

    // For segmented duckdb storage, this becomes the artifact scope directory/prefix.
    public string DuckDbFile { get; init; } = "DuckDB";

    public string AzureBlobClientName { get; init; } = "MultiProjectionOffload";

    public string AzureContainerName { get; init; } = "multiprojection-cold-events";

    public string? AzurePrefix { get; init; }
}
