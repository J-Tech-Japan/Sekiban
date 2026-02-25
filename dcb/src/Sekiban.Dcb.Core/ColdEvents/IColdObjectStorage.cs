using ResultBoxes;
namespace Sekiban.Dcb.ColdEvents;

public interface IColdObjectStorage
{
    Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct);

    Task<ResultBox<bool>> PutAsync(
        string path,
        byte[] data,
        string? expectedETag,
        CancellationToken ct);

    Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct);

    Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct);
}
