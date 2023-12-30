using Microsoft.Extensions.Configuration;
using Sekiban.Core.Setting;
namespace Sekiban.Infrastructure.Dynamo;

public class SekibanAwsOption
{
    public string Context { get; init; } = SekibanContext.Default;
    public string EventsTableId { get; init; } = "events";
    public string EventsTableIdDissolvable { get; init; } = "dissolvableevents";
    public string ItemsTableId { get; init; } = "items";
    public string ItemsTableIdDissolvable { get; init; } = "dissolvableitems";
    public string? AwsAccessKeyId { get; init; }
    public string? AwsAccessKey { get; init; }
    public string? DynamoDbRegion { get; init; }
    public string? S3BucketName { get; init; }
    public string? S3Region { get; init; }

    public static SekibanAwsOption FromConfiguration(
        IConfigurationSection section,
        IConfigurationRoot configurationRoot,
        string context = SekibanContext.Default)
    {
        var awsSection = section.GetSection("Aws");
        var eventsTableId = awsSection.GetValue<string>("DynamoEventsTable") ?? awsSection.GetValue<string>("DynamoDbEventsTable") ?? "events";
        var eventsTableIdDissolvable = awsSection.GetValue<string>("DynamoEventsTableDissolvable") ??
            awsSection.GetValue<string>("DynamoDbEventsTableDissolvable") ?? "dissolvableevents";
        var itemsTableId = awsSection.GetValue<string>("DynamoItemsTable") ?? awsSection.GetValue<string>("DynamoDbItemsTable") ?? "items";
        var itemsTableIdDissolvable = awsSection.GetValue<string>("DynamoItemsTableDissolvable") ??
            awsSection.GetValue<string>("DynamoDbItemsTableDissolvable") ?? "dissolvableitems";
        var awsAccessKeyId = awsSection.GetValue<string>("AccessKeyId") ?? awsSection.GetValue<string>("AwsAccessKeyId");
        var awsAccessKey = awsSection.GetValue<string>("AccessKey") ?? awsSection.GetValue<string>("AwsAccessKey");
        var dynamoDbRegion = awsSection.GetValue<string>("DynamoRegion") ?? awsSection.GetValue<string>("DynamoDbRegion");
        var s3BucketName = awsSection.GetValue<string>("S3BucketName");
        var s3Region = awsSection.GetValue<string>("S3Region");
        return new SekibanAwsOption
        {
            Context = context,
            EventsTableId = eventsTableId,
            EventsTableIdDissolvable = eventsTableIdDissolvable,
            ItemsTableId = itemsTableId,
            ItemsTableIdDissolvable = itemsTableIdDissolvable,
            AwsAccessKeyId = awsAccessKeyId,
            AwsAccessKey = awsAccessKey,
            DynamoDbRegion = dynamoDbRegion,
            S3Region = s3Region,
            S3BucketName = s3BucketName
        };

    }
}
