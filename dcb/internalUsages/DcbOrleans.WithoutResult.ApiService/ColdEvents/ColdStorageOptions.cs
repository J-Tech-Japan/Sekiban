namespace DcbOrleans.WithoutResult.ApiService.ColdEvents;

public sealed record ColdStorageOptions
{
    public string Type { get; init; } = "jsonl";
    public string BasePath { get; init; } = ".cold-events";
    public string JsonlDirectory { get; init; } = "jsonl";
    public string SqliteFile { get; init; } = "cold-events.sqlite";
    public string DuckDbFile { get; init; } = "cold-events.duckdb";
}
