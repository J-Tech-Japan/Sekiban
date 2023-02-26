namespace Sekiban.Core.Setting;

public interface IBlobAccessor
{
    public Task<Stream?> GetBlobAsync(SekibanBlobContainer container, Guid blobName);

    public Task<bool> SetBlobAsync(SekibanBlobContainer container, Guid blobName, Stream blob);
}
