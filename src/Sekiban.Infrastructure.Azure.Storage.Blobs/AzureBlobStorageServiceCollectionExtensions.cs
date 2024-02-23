using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Setting;
using System.Configuration;
namespace Sekiban.Infrastructure.Azure.Storage.Blobs;

public static class AzureBlobStorageServiceCollectionExtensions
{
    /// <summary>
    ///     Add Sekiban for CosmosDB
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static AzureBlobStorageOptionsServiceCollection AddSekibanAzureBlobStorage(this WebApplicationBuilder builder)
    {
        var options = SekibanCosmosDbOptions.FromConfiguration(builder.Configuration);
        return AddSekibanAzureBlobStorage(builder, options);
    }
    /// <summary>
    ///     Add Sekiban for CosmosDB
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="cosmosDbOptions"></param>
    /// <returns></returns>
    public static AzureBlobStorageOptionsServiceCollection AddSekibanAzureBlobStorage(
        this WebApplicationBuilder builder,
        SekibanCosmosDbOptions cosmosDbOptions)
    {
        AddSekibanAzureBlobStorage(builder.Services, cosmosDbOptions);
        return new AzureBlobStorageOptionsServiceCollection(cosmosDbOptions, builder);
    }
    /// <summary>
    ///     Add Sekiban for CosmosDB
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanAzureBlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var dbOptions = SekibanCosmosDbOptions.FromConfiguration(configuration);
        return AddSekibanAzureBlobStorage(services, dbOptions);
    }

    private static IServiceCollection AddSekibanAzureBlobStorage(this IServiceCollection services, SekibanCosmosDbOptions cosmosDbOptions)
    {
        services.AddSingleton(cosmosDbOptions);
        services.AddTransient<IBlobAccessor, AzureBlobAccessor>();
        services.AddTransient<IBlobContainerAccessor, AzureBlobContainerAccessor>();
        return services;
    }
    /// <summary>
    ///     Add Sekiban for CosmosDB
    /// </summary>
    /// <param name="services"></param>
    /// <param name="section"></param>
    /// <param name="configurationRoot">Configuration root to get ConnectionStrings</param>
    /// <returns></returns>
    /// <exception cref="ConfigurationErrorsException"></exception>
    public static IServiceCollection AddSekibanAzureBlobStorageFromConfigurationSection(
        this IServiceCollection services,
        IConfigurationSection section,
        IConfiguration configurationRoot)
    {
        var options = SekibanCosmosDbOptions.FromConfigurationSection(
            section,
            configurationRoot as IConfigurationRoot ?? throw new ConfigurationErrorsException("Blob Storage failed to configure."));
        return AddSekibanAzureBlobStorage(services, options);
    }
}
