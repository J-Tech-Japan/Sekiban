using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Configuration;
namespace Sekiban.Infrastructure.Cosmos;

public class SekibanCosmosDbOptions
{
    public List<SekibanAzureOption> Contexts { get; init; } = new();

    public static SekibanCosmosDbOptions Default => new();

    public static SekibanCosmosDbOptions FromConfiguration(IConfiguration configuration) =>
        FromConfigurationSection(
            configuration.GetSection("Sekiban"),
            configuration as ConfigurationRoot ?? throw new ConfigurationErrorsException("cosmos configuration failed."));
    public static SekibanCosmosDbOptions FromConfigurationSection(IConfigurationSection section, IConfigurationRoot configurationRoot)
    {
        var defaultContextSection = section.GetSection("Default");
        var contexts = section.GetSection("Contexts").GetChildren();
        var contextSettings = new List<SekibanAzureOption>();
        if (defaultContextSection.Exists())
        {
            contextSettings.Add(SekibanAzureOption.FromConfiguration(defaultContextSection, configurationRoot));
        }
        contextSettings.AddRange(
            from context in contexts
            let path = GetLastPathComponent(context)
            select SekibanAzureOption.FromConfiguration(context, configurationRoot, path));
        return new SekibanCosmosDbOptions { Contexts = contextSettings };
    }
    private static string GetLastPathComponent(IConfigurationSection section) => section.Path.Split(':').LastOrDefault() ?? section.Path;
}
public class SekibanCosmosDbOptionsServiceCollection(
    SekibanCosmosDbOptions sekibanCosmosDbOptions,
    SekibanCosmosClientOptions cosmosClientOptions,
    IServiceCollection serviceCollection)
{
    public SekibanCosmosDbOptions SekibanCosmosDbOptions { get; init; } = sekibanCosmosDbOptions;
    public SekibanCosmosClientOptions CosmosClientOptions { get; init; } = cosmosClientOptions;
    public IServiceCollection ServiceCollection { get; init; } = serviceCollection;
}
