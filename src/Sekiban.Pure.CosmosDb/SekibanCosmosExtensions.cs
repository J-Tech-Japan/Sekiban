using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.CosmosDb;

public static class SekibanCosmosExtensions
{
    public static IHostApplicationBuilder AddSekibanCosmosDb(
        this IHostApplicationBuilder builder,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        builder.Services.AddSekibanCosmosDb(builder.Configuration, optionsFunc);
        return builder;
    }
    public static IServiceCollection AddSekibanCosmosDb(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<SekibanCosmosClientOptions, SekibanCosmosClientOptions>? optionsFunc = null)
    {
        services.AddTransient<CosmosDbEventWriter>();
        services.AddTransient<IEventWriter>(sp => sp.GetRequiredService<CosmosDbEventWriter>());
        services.AddTransient<IEventRemover>(sp => sp.GetRequiredService<CosmosDbEventWriter>());
        services.AddTransient<CosmosDbFactory>();
        services.AddTransient<IEventReader, CosmosDbEventReader>();
        services.AddTransient<ICosmosMemoryCacheAccessor, CosmosMemoryCacheAccessor>();
        var dbOption = SekibanAzureCosmosDbOption.FromConfiguration(
            configuration.GetSection("Sekiban"),
            (configuration as IConfigurationRoot)!);
        var clientOption = optionsFunc is null
            ? new SekibanCosmosClientOptions()
            : optionsFunc(new SekibanCosmosClientOptions());
        services.AddSingleton(dbOption);
        services.AddMemoryCache();
        services.AddSingleton(clientOption);
        return services;
    }

}
