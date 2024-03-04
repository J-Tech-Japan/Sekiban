using Microsoft.Extensions.Configuration;
using Sekiban.Core.Setting;
namespace Sekiban.Infrastructure.Dynamo;

public class SekibanAwsOption
{
    public string Context { get; init; } = SekibanContext.Default;
    public string DynamoEventsTable { get; init; } = "events";
    public string DynamoEventsTableDissolvable { get; init; } = "dissolvableevents";
    public string DynamoItemsTable { get; init; } = "items";
    public string DynamoItemsTableDissolvable { get; init; } = "dissolvableitems";
    public string? AwsAccessKeyId { get; init; }
    public string? AwsAccessKey { get; init; }
    public string? DynamoDbRegion { get; init; }
    public static SekibanAwsOption FromConfiguration(
        IConfigurationSection section,
        IConfigurationRoot configurationRoot,
        string context = SekibanContext.Default)
    {
        var awsSection = section.GetSection("Aws");
        var eventsTableId = awsSection.GetValue<string>(nameof(DynamoEventsTable)) ?? awsSection.GetValue<string>("DynamoDbEventsTable") ?? "events";
        var eventsTableIdDissolvable = awsSection.GetValue<string>(nameof(DynamoEventsTableDissolvable)) ??
            awsSection.GetValue<string>("DynamoDbEventsTableDissolvable") ?? "dissolvableevents";
        var itemsTableId = awsSection.GetValue<string>(nameof(DynamoItemsTable)) ?? awsSection.GetValue<string>("DynamoDbItemsTable") ?? "items";
        var itemsTableIdDissolvable = awsSection.GetValue<string>(nameof(DynamoItemsTableDissolvable)) ??
            awsSection.GetValue<string>("DynamoDbItemsTableDissolvable") ?? "dissolvableitems";
        var awsAccessKeyId = awsSection.GetValue<string>("AccessKeyId") ?? awsSection.GetValue<string>(nameof(AwsAccessKeyId));
        var awsAccessKey = awsSection.GetValue<string>("AccessKey") ?? awsSection.GetValue<string>(nameof(AwsAccessKey));
        var dynamoDbRegion = awsSection.GetValue<string>("DynamoRegion") ?? awsSection.GetValue<string>(nameof(DynamoDbRegion));
        return new SekibanAwsOption
        {
            Context = context,
            DynamoEventsTable = eventsTableId,
            DynamoEventsTableDissolvable = eventsTableIdDissolvable,
            DynamoItemsTable = itemsTableId,
            DynamoItemsTableDissolvable = itemsTableIdDissolvable,
            AwsAccessKeyId = awsAccessKeyId,
            AwsAccessKey = awsAccessKey,
            DynamoDbRegion = dynamoDbRegion
        };

    }
}
