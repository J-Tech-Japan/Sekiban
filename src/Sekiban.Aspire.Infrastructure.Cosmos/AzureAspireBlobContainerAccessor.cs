using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.Infrastructure.Cosmos.Aspire;

public class AzureAspireBlobContainerAccessor(SekibanBlobAspireOptions sekibanBlobAspireOptions, IServiceProvider serviceProvider)
    : IBlobContainerAccessor
{
    public async Task<BlobContainerClient> GetContainerAsync(string containerName)
    {
        var client = serviceProvider.GetKeyedService<BlobContainerClient>(sekibanBlobAspireOptions.ConnectionName);
        if (client is null)
        {
            throw new InvalidDataException("BlobConnectionString not found");
        }
        await client.CreateIfNotExistsAsync();
        return client;
    }
    public string BlobConnectionString()
    {
        var client = serviceProvider.GetKeyedService<BlobContainerClient>(sekibanBlobAspireOptions.ConnectionName);
        return client?.Uri.AbsoluteUri ?? string.Empty;
    }
}