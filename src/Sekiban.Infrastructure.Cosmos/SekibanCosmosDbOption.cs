using Microsoft.Extensions.Configuration;
using Sekiban.Core.Setting;
namespace Sekiban.Infrastructure.Cosmos;

public record SekibanCosmosDbOption
{
    public string Context { get; init; } = SekibanContext.Default;
    public string CosmosDbEventsContainer { get; init; } = "events";
    public string CosmosDbEventsContainerDissolvable { get; init; } = "dissolvableevents";
    public string CosmosDbItemsContainer { get; init; } = "items";
    public string CosmosDbItemsContainerDissolvable { get; init; } = "dissolvableitems";
    public string? CosmosDbConnectionString { get; init; }
    public string? CosmosDbEndPointUrl { get; init; }
    public string? CosmosDbAuthorizationKey { get; init; }
    public string? CosmosDbDatabase { get; init; }
    public bool LegacyPartitions { get; init; }

    public static SekibanCosmosDbOption FromConfiguration(IConfigurationSection section, string context = SekibanContext.Default)
    {
        var eventsContainer = section.GetValue<string>("CosmosDbEventsContainer") ??
            section.GetValue<string>("AggregateEventCosmosDbContainer") ?? "events";
        var eventsContainerDissolvable = section.GetValue<string>("CosmosDbEventsContainerDissolvable") ??
            section.GetValue<string>("AggregateEventCosmosDbContainerDissolvable") ?? "dissolvableevents";
        var itemsContainer = section.GetValue<string>("CosmosDbItemsContainer") ??
            section.GetValue<string>("CosmosDbContainer") ??
            section.GetValue<string>("CosmosDbCommandsContainer") ?? section.GetValue<string>("AggregateCommandCosmosDbContainer") ?? "items";
        var itemsContainerDissolvable = section.GetValue<string>("CosmosDbItemsContainerDissolvable") ??
            section.GetValue<string>("CosmosDbContainerDissolvable") ??
            section.GetValue<string>("CosmosDbCommandsContainerDissolvable") ??
            section.GetValue<string>("AggregateCommandCosmosDbContainerDissolvable") ?? "dissolvableitems";
        var cosmosDbEndPointUrl = section.GetValue<string>("CosmosDbEndPointUrl");
        var cosmosDbAuthorizationKey = section.GetValue<string>("CosmosDbAuthorizationKey");
        var cosmosDbConnectionString = section.GetValue<string>("CosmosDbConnectionString");
        var cosmosDbDatabase = section.GetValue<string>("CosmosDbDatabase");
        return new SekibanCosmosDbOption
        {
            Context = context,
            CosmosDbEventsContainer = eventsContainer,
            CosmosDbEventsContainerDissolvable = eventsContainerDissolvable,
            CosmosDbItemsContainer = itemsContainer,
            CosmosDbItemsContainerDissolvable = itemsContainerDissolvable,
            CosmosDbConnectionString = cosmosDbConnectionString,
            CosmosDbEndPointUrl = cosmosDbEndPointUrl,
            CosmosDbAuthorizationKey = cosmosDbAuthorizationKey,
            CosmosDbDatabase = cosmosDbDatabase
        };

    }
}
