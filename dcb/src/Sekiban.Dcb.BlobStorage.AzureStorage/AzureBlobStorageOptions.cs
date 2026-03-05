namespace Sekiban.Dcb.BlobStorage.AzureStorage;

public class AzureBlobStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string ContainerName { get; set; } = "sekiban-dcb";

    public string? Prefix { get; set; }
}
