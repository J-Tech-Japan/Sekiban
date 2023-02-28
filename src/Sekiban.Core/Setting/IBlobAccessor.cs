namespace Sekiban.Core.Setting;

public interface IBlobAccessor
{
    public Task<Stream?> GetBlobAsync(SekibanBlobContainer container, string blobName);

    public Task<bool> SetBlobAsync(SekibanBlobContainer container, string blobName, Stream blob);
    public Task<Stream?> GetBlobWithGZipAsync(SekibanBlobContainer container, string blobName);

    public Task<bool> SetBlobWithGZipAsync(SekibanBlobContainer container, string blobName, Stream blob);
}
