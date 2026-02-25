namespace Sekiban.Dcb.ColdEvents;

public interface IColdEventStoreFeature
{
    Task<ColdFeatureStatus> GetStatusAsync(CancellationToken ct);
}
