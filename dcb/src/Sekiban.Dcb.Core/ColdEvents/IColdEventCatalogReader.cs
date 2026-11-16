namespace Sekiban.Dcb.ColdEvents;

public interface IColdEventCatalogReader : IColdEventStoreFeature
{
    Task<ColdDataRangeSummary> GetDataRangeSummaryAsync(
        string serviceId,
        CancellationToken ct);
}
