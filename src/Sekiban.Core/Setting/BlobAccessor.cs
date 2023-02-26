using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.Core.Setting;

public class BlobAccessor : IBlobAccessor
{
    private const string SekibanSection = "Sekiban";

    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public BlobAccessor(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

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
    public async Task<Stream?> GetBlobAsync(SekibanBlobContainer container, Guid blobName)
    {
        var containerClient = GetContainer(container.ToString());
        var blobClient = containerClient.GetBlobClient(blobName.ToString());
        return blobClient is null ? null : await blobClient.OpenReadAsync();
    }
    public async Task<bool> SetBlobAsync(SekibanBlobContainer container, Guid blobName, Stream blob)
    {
        var containerClient = GetContainer(container.ToString());
        var blobClient = containerClient.GetBlobClient(blobName.ToString());
        if (blobClient is null)
        {
            return false;
        }
        await blobClient.UploadAsync(blob);
        return true;
    }
    private string BlobConnectionString()
    {
        return _section?.GetValue<string>("BlobConnectionString") ?? throw new Exception("BlobConnectionString not found");
    }
    private BlobContainerClient GetContainer(string containerName)
    {
        return new BlobContainerClient(BlobConnectionString(), containerName.ToLower());
    }
}
