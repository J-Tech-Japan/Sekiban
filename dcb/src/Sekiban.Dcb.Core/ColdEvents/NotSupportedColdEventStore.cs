using ResultBoxes;
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

    public Task<ResultBox<ColdStoreProgress>> GetProgressAsync(string serviceId, CancellationToken ct)
        => Task.FromResult(ResultBox.Error<ColdStoreProgress>(NotSupportedException));

    public Task<ResultBox<ExportResult>> ExportIncrementalAsync(string serviceId, CancellationToken ct)
        => Task.FromResult(ResultBox.Error<ExportResult>(NotSupportedException));

    public Task<ResultBox<ColdDataRangeSummary>> GetDataRangeSummaryAsync(string serviceId, CancellationToken ct)
        => Task.FromResult(ResultBox.Error<ColdDataRangeSummary>(NotSupportedException));
}
