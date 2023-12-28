using Microsoft.Extensions.Configuration;
using Sekiban.Core.Setting;
namespace Sekiban.Infrastructure.Dynamo;

public class SekibanDynamoDbOption
{
    public string Context { get; init; } = SekibanContext.Default;
    public string EventsTableId { get; init; } = "events";
    public string EventsTableIdDissolvable { get; init; } = "dissolvableevents";
    public string ItemsTableId { get; init; } = "items";
    public string ItemsTableIdDissolvable { get; init; } = "dissolvableitems";
    public string? AwsAccessKeyId { get; init; }
    public string? AwsAccessKey { get; init; }
    public string? DynamoDbRegion { get; init; }

    public static SekibanDynamoDbOption FromConfiguration(IConfigurationSection section, string context = SekibanContext.Default)
    {
        var eventsTableId = section.GetValue<string>("DynamoDbEventsTable") ?? "events";
        var eventsTableIdDissolvable = section.GetValue<string>("DynamoDbEventsTableDissolvable") ?? "dissolvableevents";
        var itemsTableId = section.GetValue<string>("DynamoDbItemsTable") ?? "items";
        var itemsTableIdDissolvable = section.GetValue<string>("DynamoDbItemsTableDissolvable") ?? "dissolvableitems";
        var awsAccessKeyId = section.GetValue<string>("AwsAccessKeyId");
        var awsAccessKey = section.GetValue<string>("AwsAccessKey");
        var dynamoDbRegion = section.GetValue<string>("DynamoDbRegion");
        return new SekibanDynamoDbOption
        {
            Context = context,
            EventsTableId = eventsTableId,
            EventsTableIdDissolvable = eventsTableIdDissolvable,
            ItemsTableId = itemsTableId,
            ItemsTableIdDissolvable = itemsTableIdDissolvable,
            AwsAccessKeyId = awsAccessKeyId,
            AwsAccessKey = awsAccessKey,
            DynamoDbRegion = dynamoDbRegion
        };

    }
}
public class SekibanDynamoDbOptions
{
    public List<SekibanDynamoDbOption> Contexts { get; init; } = new();

    public static SekibanDynamoDbOptions Default => new();

    public static SekibanDynamoDbOptions FromConfiguration(IConfiguration configuration) =>
        FromConfigurationSection(configuration.GetSection("Sekiban"));
    public static SekibanDynamoDbOptions FromConfigurationSection(IConfigurationSection section)
    {
        var defaultContextSection = section.GetSection("Default");
        var contexts = section.GetSection("Contexts").GetChildren();
        var contextSettings = new List<SekibanDynamoDbOption>();
        if (defaultContextSection.Exists())
        {
            contextSettings.Add(SekibanDynamoDbOption.FromConfiguration(defaultContextSection));
        }
        contextSettings.AddRange(
            from context in contexts let path = GetLastPathComponent(context) select SekibanDynamoDbOption.FromConfiguration(context, path));
        return new SekibanDynamoDbOptions { Contexts = contextSettings };
    }
    private static string GetLastPathComponent(IConfigurationSection section) => section.Path.Split(':').LastOrDefault() ?? section.Path;
}
