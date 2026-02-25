using ResultBoxes;
namespace Sekiban.Dcb.ColdEvents;

public interface IColdEventCatalogReader : IColdEventStoreFeature
{
    Task<ResultBox<ColdDataRangeSummary>> GetDataRangeSummaryAsync(
        string serviceId,
        CancellationToken ct);
}
