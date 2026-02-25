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
        var path = ColdStoragePaths.ManifestPath(serviceId);
        var getResult = await storage.GetAsync(path, ct);
        if (!getResult.IsSuccess)
        {
            return null;
        }

        var obj = getResult.GetValue();
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
        var path = ColdStoragePaths.CheckpointPath(serviceId);
        var getResult = await storage.GetAsync(path, ct);
        if (!getResult.IsSuccess)
        {
            return null;
        }

        var obj = getResult.GetValue();
        var checkpoint = JsonSerializer.Deserialize<ColdCheckpoint>(obj.Data, ColdEventJsonOptions.Default);
        if (checkpoint is null)
        {
            return null;
        }
        return new CheckpointWithETag(checkpoint, obj.ETag);
    }

    public sealed record ManifestWithETag(ColdManifest Manifest, string ETag);
    public sealed record CheckpointWithETag(ColdCheckpoint Checkpoint, string ETag);
}
