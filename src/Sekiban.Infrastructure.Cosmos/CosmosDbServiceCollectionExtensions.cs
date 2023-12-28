using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Documents;
using Sekiban.Core.Setting;
using Sekiban.Infrastructure.Cosmos.Documents;
namespace Sekiban.Infrastructure.Cosmos;

public static class CosmosDbServiceCollectionExtensions
{

    public static IServiceCollection AddSekibanCosmosDB(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var options = SekibanCosmosDbOptions.FromConfiguration(configuration);
        return AddSekibanCosmosDB(services, options, optionsFunc);
    }
    public static IServiceCollection AddSekibanCosmosDBFromConfigurationSection(
        this IServiceCollection services,
        IConfigurationSection section,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var options = SekibanCosmosDbOptions.FromConfigurationSection(section);
        return AddSekibanCosmosDB(services, options, optionsFunc);
    }
    /// <summary>
    ///     Setup Sekiban for CosmosDB
    ///     can setup options for CosmosDB.
    ///     Connection string or setting will be used from appsettings.json
    /// </summary>
    /// <param name="services"></param>
    /// <param name="cosmosDbOptions"></param>
    /// <param name="optionsFunc"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanCosmosDB(
        this IServiceCollection services,
        SekibanCosmosDbOptions cosmosDbOptions,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        var options = optionsFunc is null ? new SekibanCosmosClientOptions() : optionsFunc(new SekibanCosmosClientOptions());
        services.AddSingleton(options);
        services.AddSingleton(cosmosDbOptions);
        services.AddTransient<CosmosDbFactory>();
        services.AddTransient<IDocumentPersistentWriter, CosmosDocumentWriter>();
        services.AddTransient<IDocumentPersistentRepository, CosmosDocumentRepository>();
        services.AddTransient<IDocumentRemover, CosmosDbDocumentRemover>();
        services.AddTransient<IBlobAccessor, AzureBlobAccessor>();
        return services;
    }
}
