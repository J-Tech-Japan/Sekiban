namespace Sekiban.Core.Setting;

/// <summary>
///     General Blob accessor interface.
///     Blob is using for the snapshots.
/// </summary>
public interface IBlobAccessor
{
    public Task<Stream?> GetBlobAsync(SekibanBlobContainer container, string blobName);

    public Task<bool> SetBlobAsync(SekibanBlobContainer container, string blobName, Stream blob);
    public Task<Stream?> GetBlobWithGZipAsync(SekibanBlobContainer container, string blobName);

    public Task<bool> SetBlobWithGZipAsync(SekibanBlobContainer container, string blobName, Stream blob);

    public string BlobConnectionString();
}
