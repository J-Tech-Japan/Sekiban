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
        builder.Services.AddTransient<IEventWriter, CosmosDbEventWriter>();
        builder.Services.AddTransient<CosmosDbFactory>();
        builder.Services.AddTransient<IEventReader, CosmosDbEventReader>();
        builder.Services.AddTransient<ICosmosMemoryCacheAccessor, CosmosMemoryCacheAccessor>();
        var dbOption =
            SekibanAzureCosmosDbOption.FromConfiguration(
                builder.Configuration.GetSection("Sekiban"),
                (builder.Configuration as IConfigurationRoot)!);
        var clientOption = optionsFunc is null
            ? new SekibanCosmosClientOptions()
            : optionsFunc(new SekibanCosmosClientOptions());
        builder.Services.AddSingleton(dbOption);
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(clientOption);
        return builder;
    }
}