using Azure.Storage.Blobs;
namespace Sekiban.Infrastructure.Azure.Storage.Blobs;

public class AzureBlobContainerAccessor(SekibanAzureBlobStorageOptions azureBlobStorageOptions, IServiceProvider serviceProvider)
    : IBlobContainerAccessor
{
    public async Task<BlobContainerClient> GetContainerAsync(string containerName)
    {
        var connectionString = BlobConnectionString();
        var client = new BlobContainerClient(connectionString, containerName.ToLower());
        await client.CreateIfNotExistsAsync();
        return client;
    }
    public string BlobConnectionString() =>
        azureBlobStorageOptions.GetContextOption(serviceProvider).BlobConnectionString ??
        throw new InvalidDataException("BlobConnectionString not found");
}
