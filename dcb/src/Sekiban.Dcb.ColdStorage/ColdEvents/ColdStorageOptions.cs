namespace Sekiban.Dcb.ColdEvents;

public sealed record ColdStorageOptions
{
    public string Type { get; init; } = "jsonl";

    // Relative paths are resolved against the application's current directory.
    public string BasePath { get; init; } = ColdObjectStorageFactory.DefaultBasePath;

    public string JsonlDirectory { get; init; } = "jsonl";

    public string SqliteFile { get; init; } = "cold-events.sqlite";

    public string DuckDbFile { get; init; } = "cold-events.duckdb";

    public string AzureBlobClientName { get; init; } = "MultiProjectionOffload";

    public string AzureContainerName { get; init; } = "multiprojection-cold-events";

    public string? AzurePrefix { get; init; }
}
