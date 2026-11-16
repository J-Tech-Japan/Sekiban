using Microsoft.Extensions.Options;
namespace Sekiban.Dcb.ColdEvents;

public sealed class ColdCatalogReader : IColdEventCatalogReader
{
    private readonly IColdObjectStorage _storage;
    private readonly ColdEventStoreOptions _options;

    public ColdCatalogReader(
        IColdObjectStorage storage,
        IOptions<ColdEventStoreOptions> options)
    {
        _storage = storage;
        _options = options.Value;
    }

    public Task<ColdFeatureStatus> GetStatusAsync(CancellationToken ct)
        => Task.FromResult(new ColdFeatureStatus(
            IsSupported: true,
            IsEnabled: _options.Enabled,
            Reason: _options.Enabled ? "Cold event store is active" : "Cold event store is disabled"));

    public async Task<ColdDataRangeSummary> GetDataRangeSummaryAsync(
        string serviceId,
        CancellationToken ct)
    {
        var manifest = await ColdControlFileHelper.LoadManifestAsync(_storage, serviceId, ct);

        if (manifest is null)
        {
            return new ColdDataRangeSummary(
                ServiceId: serviceId,
                OldestSortableUniqueId: null,
                LatestSortableUniqueId: null,
                TotalEventCount: 0,
                SegmentCount: 0,
                Segments: []);
        }

        return BuildSummary(serviceId, manifest);
    }

    private static ColdDataRangeSummary BuildSummary(string serviceId, ColdManifest manifest)
    {
        var segments = manifest.Segments
            .Select(s => new ColdSegmentSummary(
                Path: s.Path,
                FromSortableUniqueId: s.FromSortableUniqueId,
                ToSortableUniqueId: s.ToSortableUniqueId,
                EventCount: s.EventCount))
            .ToList();

        var oldest = manifest.Segments.Count > 0
            ? manifest.Segments[0].FromSortableUniqueId
            : null;
        var latest = manifest.Segments.Count > 0
            ? manifest.Segments[^1].ToSortableUniqueId
            : null;
        var totalEventCount = manifest.Segments.Sum(s => s.EventCount);

        return new ColdDataRangeSummary(
            ServiceId: serviceId,
            OldestSortableUniqueId: oldest,
            LatestSortableUniqueId: latest,
            TotalEventCount: totalEventCount,
            SegmentCount: manifest.Segments.Count,
            Segments: segments);
    }
}
