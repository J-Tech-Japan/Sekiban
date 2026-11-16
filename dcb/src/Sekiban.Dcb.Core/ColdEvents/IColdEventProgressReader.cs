namespace Sekiban.Dcb.ColdEvents;

public interface IColdEventProgressReader : IColdEventStoreFeature
{
    Task<ColdStoreProgress> GetProgressAsync(
        string serviceId,
        CancellationToken ct);
}
