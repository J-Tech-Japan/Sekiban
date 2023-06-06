using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ICSharpCode.SharpZipLib.GZip;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Compression;
namespace Sekiban.Core.Setting;

public class AzureBlobAccessor : IBlobAccessor
{
    private const string SekibanSection = "Sekiban";

    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    private IConfigurationSection? _section
    {
        get
        {
            var section = _configuration.GetSection(SekibanSection);
            var _sekibanContext = _serviceProvider.GetService<ISekibanContext>();
            if (!string.IsNullOrEmpty(_sekibanContext?.SettingGroupIdentifier))
            {
                section = section?.GetSection(_sekibanContext.SettingGroupIdentifier);
            }
            return section;
        }
    }

    public AzureBlobAccessor(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }
    public async Task<Stream?> GetBlobAsync(SekibanBlobContainer container, string blobName)
    {
        var containerClient = await GetContainerAsync(container.ToString());
        var blobClient = containerClient.GetBlobClient(blobName);
        return blobClient is null ? null : await blobClient.OpenReadAsync();
    }
    public async Task<bool> SetBlobAsync(SekibanBlobContainer container, string blobName, Stream blob)
    {
        var containerClient = await GetContainerAsync(container.ToString());
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
        var containerClient = await GetContainerAsync(container.ToString());
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
        var containerClient = await GetContainerAsync(container.ToString());
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

    public string BlobConnectionString() =>
        _section?.GetValue<string>("BlobConnectionString") ?? throw new Exception("BlobConnectionString not found");
    private async Task<BlobContainerClient> GetContainerAsync(string containerName)
    {
        var connectionString = BlobConnectionString();
        var client = new BlobContainerClient(connectionString, containerName.ToLower());
        await client.CreateIfNotExistsAsync();
        return client;
    }
}
