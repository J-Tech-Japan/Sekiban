namespace Sekiban.Core.Setting;

/// <summary>
///     Blob accessor with no implementation.
///     use for the test.
/// </summary>
public class NothingBlobAccessor : IBlobAccessor
{

    public async Task<Stream?> GetBlobAsync(SekibanBlobContainer container, string blobName) =>
        await Task.FromResult<Stream?>(null);
    public async Task<bool> SetBlobAsync(SekibanBlobContainer container, string blobName, Stream blob) =>
        await Task.FromResult(false);
    public async Task<Stream?> GetBlobWithGZipAsync(SekibanBlobContainer container, string blobName) =>
        await Task.FromResult<Stream?>(null);
    public async Task<bool> SetBlobWithGZipAsync(SekibanBlobContainer container, string blobName, Stream blob) =>
        await Task.FromResult(false);
    public string BlobConnectionString() => string.Empty;
}
