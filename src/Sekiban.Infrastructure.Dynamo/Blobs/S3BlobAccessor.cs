using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Azure.Storage.Blobs;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.GZip;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Setting;
using System.IO.Compression;
using System.Text;
namespace Sekiban.Infrastructure.Dynamo.Blobs;

public class S3BlobAccessor : IBlobAccessor
{
    private const string SekibanSection = "Sekiban";

    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    public S3BlobAccessor(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
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
    private async Task<AmazonS3Client> GetS3ClientAsync()
    {
        await Task.CompletedTask;
        var config = new AmazonS3Config
        {
            RegionEndpoint = S3RegionEndpoint,
        };
        return new AmazonS3Client(AwsAccessKeyId, AwsAccessKey,config);
    }
    public async Task<Stream?> GetBlobAsync(SekibanBlobContainer container, string blobName)
    {
        var client =await GetS3ClientAsync();
        var getRequest = new GetObjectRequest
        {
            BucketName = S3BucketName,
            Key = GetKey(container, blobName)
        };
        using GetObjectResponse response = await client.GetObjectAsync(getRequest);
        var stream = new MemoryStream();
        response.ResponseStream.CopyTo(stream);
        stream.Position = 0;
        return stream;
    }
    public async Task<bool> SetBlobAsync(SekibanBlobContainer container, string blobName, Stream blob)
    {
        var client = await GetS3ClientAsync();
        var putRequest = new PutObjectRequest
        {
            BucketName = S3BucketName,
            Key = GetKey(container, blobName),
            InputStream = blob
        };
        PutObjectResponse _ = await client.PutObjectAsync(putRequest);
        return true;
    }
    public async Task<Stream?> GetBlobWithGZipAsync(SekibanBlobContainer container, string blobName)
    {
        var readStream = await GetBlobAsync(container, blobName);
        if (readStream is null)
        {
            return null;
        }
        var uncompressedStream = new MemoryStream();

        var gzipStream = new GZipStream(readStream, CompressionMode.Decompress);
        await gzipStream.CopyToAsync(uncompressedStream);

        uncompressedStream.Seek(0, SeekOrigin.Begin);
        return uncompressedStream;
    }
    public async Task<bool> SetBlobWithGZipAsync(SekibanBlobContainer container, string blobName, Stream blob)
    {
        var client = await GetS3ClientAsync();
        
        await using var compressedStream = new MemoryStream();
        await using (var gZipOutputStream = new GZipOutputStream(compressedStream){IsStreamOwner = false})
        {
            await blob.CopyToAsync(gZipOutputStream);
        }

        compressedStream.Seek(0, SeekOrigin.Begin);

        var putRequest = new PutObjectRequest
        {
            BucketName = S3BucketName,
            Key = GetKey(container, blobName),
            InputStream = compressedStream,
        };
        var _ = await client.PutObjectAsync(putRequest);
        return true;
    }

    public string BlobConnectionString() => $"s3://{S3BucketName}";
    private string AwsAccessKeyId => _section?.GetValue<string>("AwsAccessKeyId") ?? throw new ArgumentNullException("AwsAccessKeyId");
    private string AwsAccessKey => _section?.GetValue<string>("AwsAccessKey") ?? throw new ArgumentNullException("AwsAccessKey");
    private string S3BucketName => _section?.GetValue<string>("S3BucketName") ?? throw new ArgumentNullException("S3BucketName");
    private string GetKey(SekibanBlobContainer container, string blobName) => $"{container.ToString().ToLower()}/{blobName}";
    private RegionEndpoint S3RegionEndpoint => RegionEndpoint.GetBySystemName(_section?.GetValue<string>("S3Region") ?? throw new ArgumentNullException("S3Region"));
}