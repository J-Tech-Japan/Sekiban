using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Setting;
using System.Configuration;
namespace Sekiban.Infrastructure.Cosmos;

public class SekibanCosmosDbOptions
{
    public List<SekibanAzureCosmosDbOption> Contexts { get; init; } = [];
    public static SekibanCosmosDbOptions Default => new();
    private static string SekibanContextIdentifier(IServiceProvider serviceProvider)
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        return sekibanContext?.SettingGroupIdentifier ?? SekibanContext.Default;
    }

    public SekibanAzureCosmosDbOption GetContextOption(string context) =>
        Contexts.Find(x => x.Context == context) ?? new SekibanAzureCosmosDbOption();
    public SekibanAzureCosmosDbOption GetContextOption(IServiceProvider serviceProvider) =>
        Contexts.Find(x => x.Context == SekibanContextIdentifier(serviceProvider)) ?? new SekibanAzureCosmosDbOption();
    public SekibanAzureCosmosDbOption GetContextOption(ISekibanContext context) =>
        Contexts.Find(x => x.Context == context.SettingGroupIdentifier) ?? new SekibanAzureCosmosDbOption();

    public static SekibanCosmosDbOptions FromConfiguration(IConfiguration configuration) =>
        FromConfigurationSection(
            configuration.GetSection("Sekiban"),
            configuration as IConfigurationRoot ?? throw new ConfigurationErrorsException("cosmos configuration failed."));
    public static SekibanCosmosDbOptions FromConfigurationSection(IConfigurationSection section, IConfigurationRoot configurationRoot)
    {
        var defaultContextSection = section.GetSection("Default");
        var contexts = section.GetSection("Contexts").GetChildren().ToList();
        var contextSettings = new List<SekibanAzureCosmosDbOption>();
        if (defaultContextSection.Exists())
        {
            contextSettings.Add(SekibanAzureCosmosDbOption.FromConfiguration(defaultContextSection, configurationRoot));
        }
        contextSettings.AddRange(
            from context in contexts
            let path = GetLastPathComponent(context)
            select SekibanAzureCosmosDbOption.FromConfiguration(context, configurationRoot, path));
        if (!defaultContextSection.Exists() && contexts.Count == 0)
        {
            contextSettings.Add(SekibanAzureCosmosDbOption.FromConfiguration(defaultContextSection, configurationRoot));
        }
        return new SekibanCosmosDbOptions { Contexts = contextSettings };
    }
    private static string GetLastPathComponent(IConfigurationSection section) => section.Path.Split(':').LastOrDefault() ?? section.Path;
}
