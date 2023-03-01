namespace Sekiban.Core.Setting;

public class NothingBlobAccessor : IBlobAccessor
{

    public async Task<Stream?> GetBlobAsync(SekibanBlobContainer container, string blobName) => await Task.FromResult<Stream?>(null);
    public async Task<bool> SetBlobAsync(SekibanBlobContainer container, string blobName, Stream blob) => await Task.FromResult(false);
    public async Task<Stream?> GetBlobWithGZipAsync(SekibanBlobContainer container, string blobName) => await Task.FromResult<Stream?>(null);
    public async Task<bool> SetBlobWithGZipAsync(SekibanBlobContainer container, string blobName, Stream blob) => await Task.FromResult(false);
}
