using ResultBoxes;
namespace Sekiban.Dcb.ColdEvents;

public interface IColdObjectStorage
{
    Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct);

    async Task<ResultBox<Stream>> OpenReadAsync(string path, CancellationToken ct)
    {
        var getResult = await GetAsync(path, ct).ConfigureAwait(false);
        if (!getResult.IsSuccess)
        {
            return ResultBox.Error<Stream>(getResult.GetException());
        }

        return ResultBox.FromValue<Stream>(new MemoryStream(getResult.GetValue().Data, writable: false));
    }

    Task<ResultBox<bool>> PutAsync(
        string path,
        Stream data,
        string? expectedETag,
        CancellationToken ct);

    Task<ResultBox<bool>> PutAsync(
        string path,
        byte[] data,
        string? expectedETag,
        CancellationToken ct);

    Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct);

    Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct);
}
