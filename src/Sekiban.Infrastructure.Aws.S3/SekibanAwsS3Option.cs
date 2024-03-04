using Microsoft.Extensions.Configuration;
using Sekiban.Core.Setting;
namespace Sekiban.Infrastructure.Aws.S3;

public class SekibanAwsS3Option
{
    public string Context { get; init; } = SekibanContext.Default;
    public string? AwsAccessKeyId { get; init; }
    public string? AwsAccessKey { get; init; }
    public string? S3BucketName { get; init; }
    public string? S3Region { get; init; }

    public static SekibanAwsS3Option FromConfiguration(
        IConfigurationSection section,
        IConfigurationRoot configurationRoot,
        string context = SekibanContext.Default)
    {
        var awsSection = section.GetSection("Aws");
        var awsAccessKeyId = awsSection.GetValue<string>("AccessKeyId") ?? awsSection.GetValue<string>("AwsAccessKeyId");
        var awsAccessKey = awsSection.GetValue<string>("AccessKey") ?? awsSection.GetValue<string>("AwsAccessKey");
        var s3BucketName = awsSection.GetValue<string>("S3BucketName");
        var s3Region = awsSection.GetValue<string>("S3Region");
        return new SekibanAwsS3Option
        {
            Context = context,
            AwsAccessKey = awsAccessKey,
            AwsAccessKeyId = awsAccessKeyId,
            S3Region = s3Region,
            S3BucketName = s3BucketName
        };

    }
}
