using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Setting;
using System.Configuration;
namespace Sekiban.Infrastructure.Azure.Storage.Blobs;

public class SekibanAzureBlobStorageOptions
{
    public List<SekibanAzureBlobStorageOption> Contexts { get; init; } = [];
    public static SekibanAzureBlobStorageOptions Default => new();
    private static string SekibanContextIdentifier(IServiceProvider serviceProvider)
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        return sekibanContext?.SettingGroupIdentifier ?? SekibanContext.Default;
    }

    public SekibanAzureBlobStorageOption GetContextOption(string context) =>
        Contexts.Find(x => x.Context == context) ?? new SekibanAzureBlobStorageOption();
    public SekibanAzureBlobStorageOption GetContextOption(IServiceProvider serviceProvider) =>
        Contexts.Find(x => x.Context == SekibanContextIdentifier(serviceProvider)) ?? new SekibanAzureBlobStorageOption();
    public SekibanAzureBlobStorageOption GetContextOption(ISekibanContext context) =>
        Contexts.Find(x => x.Context == context.SettingGroupIdentifier) ?? new SekibanAzureBlobStorageOption();

    public static SekibanAzureBlobStorageOptions FromConfiguration(IConfiguration configuration) =>
        FromConfigurationSection(
            configuration.GetSection("Sekiban"),
            configuration as IConfigurationRoot ?? throw new ConfigurationErrorsException("cosmos configuration failed."));
    public static SekibanAzureBlobStorageOptions FromConfigurationSection(IConfigurationSection section, IConfigurationRoot configurationRoot)
    {
        var defaultContextSection = section.GetSection("Default");
        var contexts = section.GetSection("Contexts").GetChildren().ToList();
        var contextSettings = new List<SekibanAzureBlobStorageOption>();
        if (defaultContextSection.Exists())
        {
            contextSettings.Add(SekibanAzureBlobStorageOption.FromConfiguration(defaultContextSection, configurationRoot));
        }
        contextSettings.AddRange(
            from context in contexts
            let path = GetLastPathComponent(context)
            select SekibanAzureBlobStorageOption.FromConfiguration(context, configurationRoot, path));
        if (!defaultContextSection.Exists() && contexts.Count == 0)
        {
            contextSettings.Add(SekibanAzureBlobStorageOption.FromConfiguration(defaultContextSection, configurationRoot));
        }
        return new SekibanAzureBlobStorageOptions { Contexts = contextSettings };
    }
    private static string GetLastPathComponent(IConfigurationSection section) => section.Path.Split(':').LastOrDefault() ?? section.Path;
}
