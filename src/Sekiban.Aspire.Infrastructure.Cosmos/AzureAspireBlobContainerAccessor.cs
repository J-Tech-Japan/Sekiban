using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Infrastructure.Azure.Storage.Blobs;
namespace Sekiban.Aspire.Infrastructure.Cosmos;

public class AzureAspireBlobContainerAccessor(SekibanBlobAspireOptions sekibanBlobAspireOptions, IServiceProvider serviceProvider)
    : IBlobContainerAccessor
{
    public async Task<BlobContainerClient> GetContainerAsync(string containerName)
    {
        var serviceClient = serviceProvider.GetKeyedService<BlobServiceClient>(sekibanBlobAspireOptions.ConnectionName);
        if (serviceClient is null)
        {
            throw new InvalidDataException("BlobConnectionString not found");
        }
        var client = serviceClient.GetBlobContainerClient(containerName.ToLower());
        await client.CreateIfNotExistsAsync();
        return client;
    }
    public string BlobConnectionString()
    {
        var client = serviceProvider.GetKeyedService<BlobContainerClient>(sekibanBlobAspireOptions.ConnectionName);
        return client?.Uri.AbsoluteUri ?? string.Empty;
    }
}
