using Azure.Storage.Blobs;
namespace Sekiban.Infrastructure.Azure.Storage.Blobs;

public interface IBlobContainerAccessor
{
    Task<BlobContainerClient> GetContainerAsync(string containerName);
    public string BlobConnectionString();
}
