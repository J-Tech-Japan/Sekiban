using Microsoft.Extensions.Configuration;
using Sekiban.Core.Setting;
namespace Sekiban.Infrastructure.Cosmos;

public record SekibanAzureOption
{
    public string Context { get; init; } = SekibanContext.Default;
    public string CosmosEventsContainer { get; init; } = "events";
    public string CosmosEventsContainerDissolvable { get; init; } = "dissolvableevents";
    public string CosmosItemsContainer { get; init; } = "items";
    public string CosmosItemsContainerDissolvable { get; init; } = "dissolvableitems";

    public string? BlobConnectionString { get; init; }
    public string? BlobConnectionStringName { get; init; }

    public string? CosmosConnectionString { get; init; }
    public string? CosmosConnectionStringName { get; init; }
    public string? CosmosEndPointUrl { get; init; }
    public string? CosmosAuthorizationKey { get; init; }
    public string? CosmosDatabase { get; init; }
    public bool LegacyPartitions { get; init; }

    public static SekibanAzureOption FromConfiguration(
        IConfigurationSection section,
        IConfigurationRoot configurationRoot,
        string context = SekibanContext.Default)
    {
        var azureSection = section.GetSection("Azure");
        var eventsContainer = azureSection.GetValue<string>("CosmosEventsContainer") ??
            azureSection.GetValue<string>("CosmosDbEventsContainer") ?? azureSection.GetValue<string>("AggregateEventCosmosDbContainer") ?? "events";
        var eventsContainerDissolvable = azureSection.GetValue<string>("CosmosEventsContainerDissolvable") ??
            azureSection.GetValue<string>("CosmosDbEventsContainerDissolvable") ??
            azureSection.GetValue<string>("AggregateEventCosmosDbContainerDissolvable") ?? "dissolvableevents";
        var itemsContainer = azureSection.GetValue<string>("CosmosItemsContainer") ??
            azureSection.GetValue<string>("CosmosDbItemsContainer") ??
            azureSection.GetValue<string>("CosmosDbContainer") ??
            azureSection.GetValue<string>("CosmosDbCommandsContainer") ??
            azureSection.GetValue<string>("AggregateCommandCosmosDbContainer") ?? "items";
        var itemsContainerDissolvable = azureSection.GetValue<string>("CosmosItemsContainerDissolvable") ??
            azureSection.GetValue<string>("CosmosDbItemsContainerDissolvable") ??
            azureSection.GetValue<string>("CosmosDbContainerDissolvable") ??
            azureSection.GetValue<string>("CosmosDbCommandsContainerDissolvable") ??
            azureSection.GetValue<string>("AggregateCommandCosmosDbContainerDissolvable") ?? "dissolvableitems";
        var cosmosEndPointUrl = azureSection.GetValue<string>("CosmosEndPointUrl") ?? azureSection.GetValue<string>("CosmosDbEndPointUrl");
        var cosmosAuthorizationKey
            = azureSection.GetValue<string>("CosmosAuthorizationKey") ?? azureSection.GetValue<string>("CosmosDbAuthorizationKey");
        var cosmosDatabase = azureSection.GetValue<string>("CosmosDatabase") ?? azureSection.GetValue<string>("CosmosDbDatabase") ?? "SekibanDb";

        var cosmosConnectionStringName = azureSection.GetValue<string>("CosmosConnectionStringName") ?? "SekibanCosmos";
        var cosmosConnectionString = configurationRoot.GetConnectionString(cosmosConnectionStringName) ??
            section.GetValue<string>("CosmosConnectionString") ?? section.GetValue<string>("CosmosDbConnectionString");

        var blobConnectionStringName = azureSection.GetValue<string>("BlobConnectionStringName") ?? "SekibanBlob";
        var blobConnectionString = configurationRoot.GetConnectionString(blobConnectionStringName) ??
            azureSection.GetValue<string>("BlobConnectionString");

        return new SekibanAzureOption
        {
            Context = context,
            CosmosEventsContainer = eventsContainer,
            CosmosEventsContainerDissolvable = eventsContainerDissolvable,
            CosmosItemsContainer = itemsContainer,
            CosmosItemsContainerDissolvable = itemsContainerDissolvable,
            CosmosConnectionString = cosmosConnectionString,
            CosmosConnectionStringName = cosmosConnectionStringName,
            CosmosEndPointUrl = cosmosEndPointUrl,
            CosmosAuthorizationKey = cosmosAuthorizationKey,
            CosmosDatabase = cosmosDatabase,
            BlobConnectionString = blobConnectionString,
            BlobConnectionStringName = blobConnectionStringName
        };

    }
}
