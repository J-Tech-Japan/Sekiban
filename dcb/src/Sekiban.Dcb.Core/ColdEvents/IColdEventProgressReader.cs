using ResultBoxes;
namespace Sekiban.Dcb.ColdEvents;

public interface IColdEventProgressReader : IColdEventStoreFeature
{
    Task<ResultBox<ColdStoreProgress>> GetProgressAsync(
        string serviceId,
        CancellationToken ct);
}
