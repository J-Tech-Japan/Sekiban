namespace Sekiban.Dcb.ColdEvents;

public sealed class NotSupportedColdEventStore :
    IColdEventProgressReader,
    IColdEventExporter,
    IColdEventCatalogReader
{
    private static readonly ColdFeatureStatus NotConfiguredStatus = new(
        IsSupported: false,
        IsEnabled: false,
        Reason: "Cold event store is not configured");

    private static readonly NotSupportedException NotSupportedException = new(
        "Cold event store is not supported");

    public Task<ColdFeatureStatus> GetStatusAsync(CancellationToken ct)
        => Task.FromResult(NotConfiguredStatus);

    public Task<ColdStoreProgress> GetProgressAsync(string serviceId, CancellationToken ct)
        => Task.FromException<ColdStoreProgress>(NotSupportedException);

    public Task<ExportResult> ExportIncrementalAsync(string serviceId, CancellationToken ct)
        => Task.FromException<ExportResult>(NotSupportedException);

    public Task<ColdDataRangeSummary> GetDataRangeSummaryAsync(string serviceId, CancellationToken ct)
        => Task.FromException<ColdDataRangeSummary>(NotSupportedException);
}
