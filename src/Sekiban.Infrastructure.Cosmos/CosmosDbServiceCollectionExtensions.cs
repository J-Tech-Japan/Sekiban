using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    public static SekibanCosmosDbOptionsServiceCollection AddSekibanCosmosDb(
        this IHostApplicationBuilder builder,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var options = SekibanCosmosDbOptions.FromConfiguration(builder.Configuration);
        var blobOptions = SekibanAzureBlobStorageOptions.FromConfiguration(builder.Configuration);
        return AddSekibanCosmosDb(builder, options, blobOptions, optionsFunc);
    }
    public static SekibanCosmosDbOptionsServiceCollection AddSekibanCosmosDbWithoutBlob(
        this HostApplicationBuilder builder,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var options = SekibanCosmosDbOptions.FromConfiguration(builder.Configuration);
        var clientOptions = optionsFunc is null
            ? new SekibanCosmosClientOptions()
            : optionsFunc(new SekibanCosmosClientOptions());
        builder.Services.AddSekibanCosmosDbWithoutBlob(options, clientOptions);
        return new SekibanCosmosDbOptionsServiceCollection(options, clientOptions, builder);
    }

    /// <summary>
    ///     Add Sekiban for CosmosDB
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="cosmosDbOptions"></param>
    /// <param name="azureBlobStorageOptions"></param>
    /// <param name="optionsFunc"></param>
    /// <returns></returns>
    public static SekibanCosmosDbOptionsServiceCollection AddSekibanCosmosDb(
        this IHostApplicationBuilder builder,
        SekibanCosmosDbOptions cosmosDbOptions,
        SekibanAzureBlobStorageOptions azureBlobStorageOptions,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var options = optionsFunc is null
            ? new SekibanCosmosClientOptions()
            : optionsFunc(new SekibanCosmosClientOptions());
        AddSekibanCosmosDb(builder.Services, cosmosDbOptions, azureBlobStorageOptions, options);
        return new SekibanCosmosDbOptionsServiceCollection(cosmosDbOptions, options, builder);
    }
    /// <summary>
    ///     Add Sekiban for CosmosDB
    ///     It also set up azure blob
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <param name="optionsFunc"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanCosmosDb(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var dbOptions = SekibanCosmosDbOptions.FromConfiguration(configuration);
        var blobOptions = SekibanAzureBlobStorageOptions.FromConfiguration(configuration);
        var options = optionsFunc is null
            ? new SekibanCosmosClientOptions()
            : optionsFunc(new SekibanCosmosClientOptions());
        return AddSekibanCosmosDb(services, dbOptions, blobOptions, options);
    }
    /// <summary>
    ///     Add Sekiban for CosmosDB
    ///     It doesn't set up blob, so you need to add it separately
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <param name="optionsFunc"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanCosmosDbWithoutBlob(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var dbOptions = SekibanCosmosDbOptions.FromConfiguration(configuration);
        var options = optionsFunc is null
            ? new SekibanCosmosClientOptions()
            : optionsFunc(new SekibanCosmosClientOptions());
        return AddSekibanCosmosDbWithoutBlob(services, dbOptions, options);
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
    public static IServiceCollection AddSekibanCosmosDb(
        this IServiceCollection services,
        SekibanCosmosDbOptions cosmosDbOptions,
        SekibanAzureBlobStorageOptions azureBlobStorageOptions,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var options = optionsFunc is null
            ? new SekibanCosmosClientOptions()
            : optionsFunc(new SekibanCosmosClientOptions());
        return AddSekibanCosmosDb(services, cosmosDbOptions, azureBlobStorageOptions, options);
    }
    /// <summary>
    ///     Setup Sekiban for CosmosDB
    ///     can setup options for CosmosDB.
    ///     Connection string or setting will be used from appsettings.json
    /// </summary>
    /// <param name="services"></param>
    /// <param name="cosmosDbOptions"></param>
    /// <param name="azureBlobStorageOptions"></param>
    /// <param name="options"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanCosmosDb(
        this IServiceCollection services,
        SekibanCosmosDbOptions cosmosDbOptions,
        SekibanAzureBlobStorageOptions azureBlobStorageOptions,
        SekibanCosmosClientOptions? options = null)
    {
        // CosmosDB
        services.AddSekibanCosmosDbWithoutBlob(cosmosDbOptions, options);
        // Azure Blob
        services.AddSekibanAzureBlobStorage(azureBlobStorageOptions);
        return services;
    }
    /// <summary>
    ///     Setup Sekiban for CosmosDB without Azure Blob
    ///     can setup options for CosmosDB.
    ///     Connection string or setting will be used from appsettings.json
    /// </summary>
    /// <param name="services"></param>
    /// <param name="cosmosDbOptions"></param>
    /// <param name="options"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanCosmosDbWithoutBlob(
        this IServiceCollection services,
        SekibanCosmosDbOptions cosmosDbOptions,
        SekibanCosmosClientOptions? options = null)
    {
        services.AddSingleton(options ?? new SekibanCosmosClientOptions());
        services.AddSingleton(cosmosDbOptions);
        services.AddTransient<ICosmosDbFactory, CosmosDbFactory>();
        services.AddTransient<IDocumentPersistentWriter, CosmosDocumentWriter>();
        services.AddTransient<IDocumentPersistentRepository, CosmosDocumentRepository>();
        services.AddTransient<IDocumentRemover, CosmosDbDocumentRemover>();
        services.AddTransient<IEventPersistentWriter, CosmosDocumentWriter>();
        services.AddTransient<IEventPersistentRepository, CosmosDocumentRepository>();
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
    public static IServiceCollection AddSekibanCosmosDbFromConfigurationSection(
        this IServiceCollection services,
        IConfigurationSection section,
        IConfiguration configurationRoot,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var options = SekibanCosmosDbOptions.FromConfigurationSection(
            section,
            configurationRoot as IConfigurationRoot ??
            throw new ConfigurationErrorsException("cosmos db failed to configure."));
        var blobOptions = SekibanAzureBlobStorageOptions.FromConfigurationSection(
            section,
            configurationRoot as IConfigurationRoot ??
            throw new ConfigurationErrorsException("cosmos db failed to configure."));
        return AddSekibanCosmosDb(services, options, blobOptions, optionsFunc);
    }
}
