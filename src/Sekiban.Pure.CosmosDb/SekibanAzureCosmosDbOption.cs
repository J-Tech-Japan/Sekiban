using Microsoft.Extensions.Configuration;

namespace Sekiban.Pure.CosmosDb;

public record SekibanAzureCosmosDbOption
{
    public const string CosmosEventsContainerDefaultValue = "events";
    public const string CosmosItemsContainerDefaultValue = "items";
    public const string CosmosConnectionStringNameDefaultValue = "SekibanCosmos";
    public const string CosmosDatabaseDefaultValue = "SekibanDb";
    public const bool LegacyPartitionDefaultValue = false;

    public string CosmosEventsContainer { get; init; } = CosmosEventsContainerDefaultValue;
    public string CosmosItemsContainer { get; init; } = CosmosItemsContainerDefaultValue;
    public string? CosmosEndPointUrl { get; init; }
    public string? CosmosAuthorizationKey { get; init; }
    public string CosmosConnectionStringName { get; init; } = CosmosConnectionStringNameDefaultValue;
    public string? CosmosConnectionString { get; init; }
    public string CosmosDatabase { get; init; } = CosmosDatabaseDefaultValue;

    public bool LegacyPartitions { get; init; }

    public static SekibanAzureCosmosDbOption FromConfiguration(
        IConfigurationSection section,
        IConfigurationRoot configurationRoot)
    {
        var azureSection = section.GetSection("Azure");

        var eventsContainer = azureSection.GetValue<string>(nameof(CosmosEventsContainer)) ??
                              azureSection.GetValue<string>("CosmosDbEventsContainer") ??
                              azureSection.GetValue<string>("AggregateEventCosmosDbContainer") ?? CosmosEventsContainerDefaultValue;
        var itemsContainer = azureSection.GetValue<string>(nameof(CosmosItemsContainer)) ??
                             azureSection.GetValue<string>("CosmosDbItemsContainer") ??
                             azureSection.GetValue<string>("CosmosDbContainer") ??
                             azureSection.GetValue<string>("CosmosDbCommandsContainer") ??
                             azureSection.GetValue<string>("AggregateCommandCosmosDbContainer") ?? CosmosItemsContainerDefaultValue;

        var cosmosEndPointUrl = azureSection.GetValue<string>(nameof(CosmosEndPointUrl)) ?? azureSection.GetValue<string>("CosmosDbEndPointUrl");
        var cosmosAuthorizationKey = azureSection.GetValue<string>(nameof(CosmosAuthorizationKey)) ??
                                     azureSection.GetValue<string>("CosmosDbAuthorizationKey");
        var cosmosConnectionStringName = azureSection.GetValue<string>(nameof(CosmosConnectionStringName)) ?? CosmosConnectionStringNameDefaultValue;
        var cosmosConnectionString = configurationRoot.GetConnectionString(cosmosConnectionStringName) ??
                                     section.GetValue<string>(nameof(CosmosConnectionString)) ?? section.GetValue<string>("CosmosDbConnectionString");
        var cosmosDatabase = azureSection.GetValue<string>(nameof(CosmosDatabase)) ??
                             azureSection.GetValue<string>("CosmosDbDatabase") ?? CosmosDatabaseDefaultValue;


        var legacyPartition = azureSection.GetValue<bool?>(nameof(LegacyPartitions)) ?? LegacyPartitionDefaultValue;

        return new SekibanAzureCosmosDbOption
        {
            CosmosEventsContainer = eventsContainer,
            CosmosItemsContainer = itemsContainer,
            CosmosConnectionString = cosmosConnectionString,
            CosmosConnectionStringName = cosmosConnectionStringName,
            CosmosEndPointUrl = cosmosEndPointUrl,
            CosmosAuthorizationKey = cosmosAuthorizationKey,
            CosmosDatabase = cosmosDatabase,
            LegacyPartitions = legacyPartition
        };
    }
}