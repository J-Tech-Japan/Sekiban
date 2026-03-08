namespace Sekiban.Dcb.ColdEvents;

/// Centralized path construction for cold storage control and segment files.
public static class ColdStoragePaths
{
    public static string ManifestPath(string serviceId) =>
        $"control/{serviceId}/manifest.json";

    public static string CheckpointPath(string serviceId) =>
        $"control/{serviceId}/checkpoint.json";

    public static string SegmentPath(string serviceId, string from, string to, string extension = ".jsonl") =>
        $"segments/{serviceId}/{from}_{to}{NormalizeExtension(extension)}";

    private static string NormalizeExtension(string extension)
        => string.IsNullOrWhiteSpace(extension)
            ? ".jsonl"
            : extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
}
