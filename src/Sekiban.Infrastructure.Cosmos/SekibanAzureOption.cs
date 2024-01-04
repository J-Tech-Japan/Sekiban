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

    public string? CosmosEndPointUrl { get; init; }
    public string? CosmosAuthorizationKey { get; init; }
    public string CosmosConnectionStringName { get; init; } = "SekibanCosmos";
    public string? CosmosConnectionString { get; init; }
    public string CosmosDatabase { get; init; } = "SekibanDb";

    public string BlobConnectionStringName { get; init; } = "SekibanBlob";
    public string? BlobConnectionString { get; init; }

    public bool LegacyPartitions { get; init; }

    public static SekibanAzureOption FromConfiguration(
        IConfigurationSection section,
        IConfigurationRoot configurationRoot,
        string context = SekibanContext.Default)
    {
        var azureSection = section.GetSection("Azure");

        var eventsContainer = azureSection.GetValue<string>(nameof(CosmosEventsContainer))
            ?? azureSection.GetValue<string>("CosmosDbEventsContainer")
            ?? azureSection.GetValue<string>("AggregateEventCosmosDbContainer")
            ?? "events";
        var eventsContainerDissolvable = azureSection.GetValue<string>(nameof(CosmosEventsContainerDissolvable))
            ?? azureSection.GetValue<string>("CosmosDbEventsContainerDissolvable")
            ?? azureSection.GetValue<string>("AggregateEventCosmosDbContainerDissolvable")
            ?? "dissolvableevents";
        var itemsContainer = azureSection.GetValue<string>(nameof(CosmosItemsContainer))
            ?? azureSection.GetValue<string>("CosmosDbItemsContainer")
            ?? azureSection.GetValue<string>("CosmosDbContainer")
            ?? azureSection.GetValue<string>("CosmosDbCommandsContainer")
            ?? azureSection.GetValue<string>("AggregateCommandCosmosDbContainer")
            ?? "items";
        var itemsContainerDissolvable = azureSection.GetValue<string>(nameof(CosmosItemsContainerDissolvable))
            ?? azureSection.GetValue<string>("CosmosDbItemsContainerDissolvable")
            ?? azureSection.GetValue<string>("CosmosDbContainerDissolvable")
            ?? azureSection.GetValue<string>("CosmosDbCommandsContainerDissolvable")
            ?? azureSection.GetValue<string>("AggregateCommandCosmosDbContainerDissolvable")
            ?? "dissolvableitems";

        var cosmosEndPointUrl = azureSection.GetValue<string>(nameof(CosmosEndPointUrl))
            ?? azureSection.GetValue<string>("CosmosDbEndPointUrl");
        var cosmosAuthorizationKey = azureSection.GetValue<string>(nameof(CosmosAuthorizationKey))
            ?? azureSection.GetValue<string>("CosmosDbAuthorizationKey");
        var cosmosConnectionStringName = azureSection.GetValue<string>(nameof(CosmosConnectionStringName))
            ?? "SekibanCosmos";
        var cosmosConnectionString = configurationRoot.GetConnectionString(cosmosConnectionStringName)
            ?? section.GetValue<string>(nameof(CosmosConnectionString))
            ?? section.GetValue<string>("CosmosDbConnectionString");
        var cosmosDatabase = azureSection.GetValue<string>(nameof(CosmosDatabase))
            ?? azureSection.GetValue<string>("CosmosDbDatabase")
            ?? "SekibanDb";

        var blobConnectionStringName = azureSection.GetValue<string>(nameof(BlobConnectionStringName))
            ?? "SekibanBlob";
        var blobConnectionString = configurationRoot.GetConnectionString(blobConnectionStringName)
            ?? azureSection.GetValue<string>(nameof(BlobConnectionString));

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
