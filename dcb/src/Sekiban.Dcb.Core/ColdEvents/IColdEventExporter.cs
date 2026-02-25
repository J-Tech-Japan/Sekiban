using ResultBoxes;
namespace Sekiban.Dcb.ColdEvents;

public interface IColdEventExporter : IColdEventStoreFeature
{
    Task<ResultBox<ExportResult>> ExportIncrementalAsync(
        string serviceId,
        CancellationToken ct);
}
