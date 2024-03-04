using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Documents;
using Sekiban.Infrastructure.Azure.Storage.Blobs;
using Sekiban.Infrastructure.Cosmos.Documents;
using System.Configuration;
namespace Sekiban.Infrastructure.Cosmos;

public static class CosmosDbServiceCollectionExtensions
{
    /// <summary>
    ///     Add Sekiban for CosmosDB
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="optionsFunc"></param>
    /// <returns></returns>
    public static SekibanCosmosDbOptionsServiceCollection AddSekibanCosmosDB(
        this WebApplicationBuilder builder,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var options = SekibanCosmosDbOptions.FromConfiguration(builder.Configuration);
        var blobOptions = SekibanAzureBlobStorageOptions.FromConfiguration(builder.Configuration);
        return AddSekibanCosmosDB(builder, options, blobOptions, optionsFunc);
    }
    /// <summary>
    ///     Add Sekiban for CosmosDB
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="cosmosDbOptions"></param>
    /// <param name="azureBlobStorageOptions"></param>
    /// <param name="optionsFunc"></param>
    /// <returns></returns>
    public static SekibanCosmosDbOptionsServiceCollection AddSekibanCosmosDB(
        this WebApplicationBuilder builder,
        SekibanCosmosDbOptions cosmosDbOptions,
        SekibanAzureBlobStorageOptions azureBlobStorageOptions,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var options = optionsFunc is null ? new SekibanCosmosClientOptions() : optionsFunc(new SekibanCosmosClientOptions());
        AddSekibanCosmosDB(builder.Services, cosmosDbOptions, azureBlobStorageOptions, options);
        return new SekibanCosmosDbOptionsServiceCollection(cosmosDbOptions, options, builder);
    }
    /// <summary>
    ///     Add Sekiban for CosmosDB
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <param name="optionsFunc"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanCosmosDB(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var dbOptions = SekibanCosmosDbOptions.FromConfiguration(configuration);
        var blobOptions = SekibanAzureBlobStorageOptions.FromConfiguration(configuration);
        var options = optionsFunc is null ? new SekibanCosmosClientOptions() : optionsFunc(new SekibanCosmosClientOptions());
        return AddSekibanCosmosDB(services, dbOptions, blobOptions, options);
    }
    /// <summary>
    ///     Setup Sekiban for CosmosDB
    ///     can setup options for CosmosDB.
    ///     Connection string or setting will be used from appsettings.json
    /// </summary>
    /// <param name="services"></param>
    /// <param name="cosmosDbOptions"></param>
    /// <param name="azureBlobStorageOptions"></param>
    /// <param name="optionsFunc"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanCosmosDB(
        this IServiceCollection services,
        SekibanCosmosDbOptions cosmosDbOptions,
        SekibanAzureBlobStorageOptions azureBlobStorageOptions,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var options = optionsFunc is null ? new SekibanCosmosClientOptions() : optionsFunc(new SekibanCosmosClientOptions());
        return AddSekibanCosmosDB(services, cosmosDbOptions, azureBlobStorageOptions, options);
    }
    private static IServiceCollection AddSekibanCosmosDB(
        this IServiceCollection services,
        SekibanCosmosDbOptions cosmosDbOptions,
        SekibanAzureBlobStorageOptions azureBlobStorageOptions,
        SekibanCosmosClientOptions? options = null)
    {
        services.AddSingleton(options ?? new SekibanCosmosClientOptions());
        services.AddSingleton(cosmosDbOptions);
        services.AddTransient<ICosmosDbFactory, CosmosDbFactory>();
        services.AddTransient<IDocumentPersistentWriter, CosmosDocumentWriter>();
        services.AddTransient<IDocumentPersistentRepository, CosmosDocumentRepository>();
        services.AddTransient<IDocumentRemover, CosmosDbDocumentRemover>();
        services.AddSekibanAzureBlobStorage(azureBlobStorageOptions);
        return services;
    }
    /// <summary>
    ///     Add Sekiban for CosmosDB
    /// </summary>
    /// <param name="services"></param>
    /// <param name="section"></param>
    /// <param name="configurationRoot">Configuration root to get ConnectionStrings</param>
    /// <param name="optionsFunc"></param>
    /// <returns></returns>
    /// <exception cref="ConfigurationErrorsException"></exception>
    public static IServiceCollection AddSekibanCosmosDBFromConfigurationSection(
        this IServiceCollection services,
        IConfigurationSection section,
        IConfiguration configurationRoot,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var options = SekibanCosmosDbOptions.FromConfigurationSection(
            section,
            configurationRoot as IConfigurationRoot ?? throw new ConfigurationErrorsException("cosmos db failed to configure."));
        var blobOptions = SekibanAzureBlobStorageOptions.FromConfigurationSection(
            section,
            configurationRoot as IConfigurationRoot ?? throw new ConfigurationErrorsException("cosmos db failed to configure."));
        return AddSekibanCosmosDB(services, options, blobOptions, optionsFunc);
    }
}
