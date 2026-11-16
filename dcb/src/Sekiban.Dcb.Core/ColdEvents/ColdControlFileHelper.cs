using System.Text.Json;
namespace Sekiban.Dcb.ColdEvents;

/// Shared logic for loading manifest and checkpoint control files from cold storage.
public static class ColdControlFileHelper
{
    public static async Task<ColdManifest?> LoadManifestAsync(
        IColdObjectStorage storage,
        string serviceId,
        CancellationToken ct)
    {
        var result = await LoadManifestWithETagAsync(storage, serviceId, ct);
        return result?.Manifest;
    }

    public static async Task<ManifestWithETag?> LoadManifestWithETagAsync(
        IColdObjectStorage storage,
        string serviceId,
        CancellationToken ct)
    {
        var obj = await TryGetAsync(storage, ColdStoragePaths.ManifestPath(serviceId), ct);
        if (obj is null)
        {
            return null;
        }

        var manifest = JsonSerializer.Deserialize<ColdManifest>(obj.Data, ColdEventJsonOptions.Default);
        if (manifest is null)
        {
            return null;
        }
        return new ManifestWithETag(manifest, obj.ETag);
    }

    public static async Task<ColdCheckpoint?> LoadCheckpointAsync(
        IColdObjectStorage storage,
        string serviceId,
        CancellationToken ct)
    {
        var result = await LoadCheckpointWithETagAsync(storage, serviceId, ct);
        return result?.Checkpoint;
    }

    public static async Task<CheckpointWithETag?> LoadCheckpointWithETagAsync(
        IColdObjectStorage storage,
        string serviceId,
        CancellationToken ct)
    {
        var obj = await TryGetAsync(storage, ColdStoragePaths.CheckpointPath(serviceId), ct);
        if (obj is null)
        {
            return null;
        }

        var checkpoint = JsonSerializer.Deserialize<ColdCheckpoint>(obj.Data, ColdEventJsonOptions.Default);
        if (checkpoint is null)
        {
            return null;
        }
        return new CheckpointWithETag(checkpoint, obj.ETag);
    }

    /// <summary>
    ///     A control file that is missing or cannot be read is treated as absent (the behaviour callers rely on),
    ///     including a provider timeout. Only the caller's own cancellation propagates.
    /// </summary>
    private static async Task<ColdStorageObject?> TryGetAsync(IColdObjectStorage storage, string path, CancellationToken ct)
    {
        try
        {
            return await storage.GetAsync(path, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public sealed record ManifestWithETag(ColdManifest Manifest, string ETag);
    public sealed record CheckpointWithETag(ColdCheckpoint Checkpoint, string ETag);
}
