using Azure.Storage.Blobs;
using Sekiban.Dcb.Snapshots;

namespace SekibanDcbOrleans.ApiService;

public class MinimalBlobStorageSnapshotAccessor : IBlobStorageSnapshotAccessor
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName = "multiprojection-snapshots";
    private BlobContainerClient? _containerClient;

    public string ProviderName => "AzureBlobStorage";

    public MinimalBlobStorageSnapshotAccessor(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient;
    }

    private async Task<BlobContainerClient> GetContainerClientAsync()
    {
        if (_containerClient == null)
        {
            _containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            await _containerClient.CreateIfNotExistsAsync();
        }
        return _containerClient;
    }

    public async Task<byte[]> ReadAsync(string blobName, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = await GetContainerClientAsync();
            var blobClient = containerClient.GetBlobClient(blobName);
            
            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return Array.Empty<byte>();
            }

            var response = await blobClient.DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<byte>();
        }
    }

    public async Task<string> WriteAsync(byte[] data, string blobName, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = await GetContainerClientAsync();
            var blobClient = containerClient.GetBlobClient(blobName);
            
            using var stream = new MemoryStream(data);
            var response = await blobClient.UploadAsync(stream, overwrite: true, cancellationToken);
            return blobClient.Uri.ToString();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}