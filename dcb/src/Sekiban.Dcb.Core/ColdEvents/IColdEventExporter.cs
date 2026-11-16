namespace Sekiban.Dcb.ColdEvents;

public interface IColdEventExporter : IColdEventStoreFeature
{
    Task<ExportResult> ExportIncrementalAsync(
        string serviceId,
        CancellationToken ct);
}
