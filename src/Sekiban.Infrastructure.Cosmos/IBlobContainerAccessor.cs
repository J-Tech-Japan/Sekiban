using Azure.Storage.Blobs;
namespace Sekiban.Infrastructure.Cosmos;

public interface IBlobContainerAccessor
{
    Task<BlobContainerClient> GetContainerAsync(string containerName);
    public string BlobConnectionString();
}
