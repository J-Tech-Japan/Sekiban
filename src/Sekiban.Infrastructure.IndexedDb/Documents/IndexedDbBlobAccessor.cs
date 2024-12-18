using Sekiban.Core.Setting;

namespace Sekiban.Infrastructure.IndexedDb.Documents;

public class IndexedDbBlobAccessor : IBlobAccessor
{
    public Task<Stream?> GetBlobAsync(SekibanBlobContainer container, string blobName)
    {
        throw new NotImplementedException();
    }

    public Task<Stream?> GetBlobWithGZipAsync(SekibanBlobContainer container, string blobName)
    {
        throw new NotImplementedException();
    }

    public Task<bool> SetBlobAsync(SekibanBlobContainer container, string blobName, Stream blob)
    {
        throw new NotImplementedException();
    }

    public Task<bool> SetBlobWithGZipAsync(SekibanBlobContainer container, string blobName, Stream blob)
    {
        throw new NotImplementedException();
    }

    public string BlobConnectionString() => string.Empty;
}
