using Azure.Storage.Blobs;
namespace Sekiban.Infrastructure.Cosmos;

public class AzureBlobContainerAccessor(SekibanCosmosDbOptions cosmosDbOptions, IServiceProvider serviceProvider) : IBlobContainerAccessor
{
    public async Task<BlobContainerClient> GetContainerAsync(string containerName)
    {
        var connectionString = BlobConnectionString();
        var client = new BlobContainerClient(connectionString, containerName.ToLower());
        await client.CreateIfNotExistsAsync();
        return client;
    }
    public string BlobConnectionString() =>
        cosmosDbOptions.GetContextOption(serviceProvider).BlobConnectionString ?? throw new InvalidDataException("BlobConnectionString not found");
}
