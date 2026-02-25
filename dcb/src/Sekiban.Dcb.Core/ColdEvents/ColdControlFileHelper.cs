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
        var path = ColdStoragePaths.CheckpointPath(serviceId);
        var getResult = await storage.GetAsync(path, ct);
        if (!getResult.IsSuccess)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ColdCheckpoint>(getResult.GetValue().Data, ColdEventJsonOptions.Default);
    }

    public sealed record ManifestWithETag(ColdManifest Manifest, string ETag);
}
