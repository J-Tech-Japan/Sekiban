using LanguageExt.TypeClasses;
using Microsoft.Extensions.Configuration;
using Sekiban.Core.Setting;
namespace Sekiban.Infrastructure.Cosmos;

public record SekibanAzureOption
{
    public static readonly string CosmosEventsContainerDefaultValue = "events";
    public static readonly string CosmosEventsContainerDissolvableDefaultValue = "dissolvableevents";
    public static readonly string CosmosItemsContainerDefaultValue = "items";
    public static readonly string CosmosItemsContainerDissolvableDefaultValue = "dissolvableitems";
    public static readonly string CosmosConnectionStringNameDefaultValue = "SekibanCosmos";
    public static readonly string CosmosDatabaseDefaultValue = "SekibanDb";
    public static readonly string BlobConnectionStringNameDefaultValue = "SekibanBlob";

    public string Context { get; init; } = SekibanContext.Default;

    public string CosmosEventsContainer { get; init; } = CosmosEventsContainerDefaultValue;
    public string CosmosEventsContainerDissolvable { get; init; } = CosmosEventsContainerDissolvableDefaultValue;
    public string CosmosItemsContainer { get; init; } = CosmosItemsContainerDefaultValue;
    public string CosmosItemsContainerDissolvable { get; init; } = CosmosItemsContainerDissolvableDefaultValue;

    public string? CosmosEndPointUrl { get; init; }
    public string? CosmosAuthorizationKey { get; init; }
    public string CosmosConnectionStringName { get; init; } = CosmosConnectionStringNameDefaultValue;
    public string? CosmosConnectionString { get; init; }
    public string CosmosDatabase { get; init; } = CosmosDatabaseDefaultValue;

    public string BlobConnectionStringName { get; init; } = BlobConnectionStringNameDefaultValue;
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
            ?? CosmosEventsContainerDefaultValue;
        var eventsContainerDissolvable = azureSection.GetValue<string>(nameof(CosmosEventsContainerDissolvable))
            ?? azureSection.GetValue<string>("CosmosDbEventsContainerDissolvable")
            ?? azureSection.GetValue<string>("AggregateEventCosmosDbContainerDissolvable")
            ?? CosmosEventsContainerDissolvableDefaultValue;
        var itemsContainer = azureSection.GetValue<string>(nameof(CosmosItemsContainer))
            ?? azureSection.GetValue<string>("CosmosDbItemsContainer")
            ?? azureSection.GetValue<string>("CosmosDbContainer")
            ?? azureSection.GetValue<string>("CosmosDbCommandsContainer")
            ?? azureSection.GetValue<string>("AggregateCommandCosmosDbContainer")
            ?? CosmosItemsContainerDefaultValue;
        var itemsContainerDissolvable = azureSection.GetValue<string>(nameof(CosmosItemsContainerDissolvable))
            ?? azureSection.GetValue<string>("CosmosDbItemsContainerDissolvable")
            ?? azureSection.GetValue<string>("CosmosDbContainerDissolvable")
            ?? azureSection.GetValue<string>("CosmosDbCommandsContainerDissolvable")
            ?? azureSection.GetValue<string>("AggregateCommandCosmosDbContainerDissolvable")
            ?? CosmosItemsContainerDissolvableDefaultValue;

        var cosmosEndPointUrl = azureSection.GetValue<string>(nameof(CosmosEndPointUrl))
            ?? azureSection.GetValue<string>("CosmosDbEndPointUrl");
        var cosmosAuthorizationKey = azureSection.GetValue<string>(nameof(CosmosAuthorizationKey))
            ?? azureSection.GetValue<string>("CosmosDbAuthorizationKey");
        var cosmosConnectionStringName = azureSection.GetValue<string>(nameof(CosmosConnectionStringName))
            ?? CosmosConnectionStringNameDefaultValue;
        var cosmosConnectionString = configurationRoot.GetConnectionString(cosmosConnectionStringName)
            ?? section.GetValue<string>(nameof(CosmosConnectionString))
            ?? section.GetValue<string>("CosmosDbConnectionString");
        var cosmosDatabase = azureSection.GetValue<string>(nameof(CosmosDatabase))
            ?? azureSection.GetValue<string>("CosmosDbDatabase")
            ?? CosmosDatabaseDefaultValue;

        var blobConnectionStringName = azureSection.GetValue<string>(nameof(BlobConnectionStringName))
            ?? BlobConnectionStringNameDefaultValue;
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
