using Azure.Storage.Blobs.Models;
using ICSharpCode.SharpZipLib.GZip;
using Sekiban.Core.Setting;
using System.IO.Compression;
namespace Sekiban.Infrastructure.Cosmos;

/// <summary>
///     Blob accessor implementation for Azure Blob Storage.
/// </summary>
public class AzureBlobAccessor(IBlobContainerAccessor blobContainerAccessor) : IBlobAccessor
{
    public async Task<Stream?> GetBlobAsync(SekibanBlobContainer container, string blobName)
    {
        var containerClient = await blobContainerAccessor.GetContainerAsync(container.ToString());
        var blobClient = containerClient.GetBlobClient(blobName);
        return blobClient is null ? null : await blobClient.OpenReadAsync();
    }
    public async Task<bool> SetBlobAsync(SekibanBlobContainer container, string blobName, Stream blob)
    {
        var containerClient = await blobContainerAccessor.GetContainerAsync(container.ToString());
        var blobClient = containerClient.GetBlobClient(blobName);
        if (blobClient is null)
        {
            return false;
        }
        await blobClient.UploadAsync(blob);
        return true;
    }
    public async Task<Stream?> GetBlobWithGZipAsync(SekibanBlobContainer container, string blobName)
    {
        var containerClient = await blobContainerAccessor.GetContainerAsync(container.ToString());
        var blobClient = containerClient.GetBlobClient(blobName);
        if (blobClient is null)
        {
            return null;
        }
        var uncompressedStream = new MemoryStream();
        var gzipStream = new GZipStream(await blobClient.OpenReadAsync(), CompressionMode.Decompress);
        await gzipStream.CopyToAsync(uncompressedStream);
        uncompressedStream.Position = 0;
        return uncompressedStream;
    }
    public async Task<bool> SetBlobWithGZipAsync(SekibanBlobContainer container, string blobName, Stream blob)
    {
        var containerClient = await blobContainerAccessor.GetContainerAsync(container.ToString());
        var blobClient = containerClient.GetBlobClient(blobName);
        if (blobClient is null)
        {
            return false;
        }
        var compressedStream = new MemoryStream();

        await using (var gZipOutputStream = new GZipOutputStream(compressedStream) { IsStreamOwner = false })
        {
            await blob.CopyToAsync(gZipOutputStream);
        }
        compressedStream.Seek(0, SeekOrigin.Begin);
        await blobClient.UploadAsync(
            compressedStream,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "application/x-gzip" } });
        return true;
    }
    public string BlobConnectionString() => blobContainerAccessor.BlobConnectionString();
}
