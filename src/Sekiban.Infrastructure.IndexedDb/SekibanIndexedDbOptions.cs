using System.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Setting;

namespace Sekiban.Infrastructure.IndexedDb;

public class SekibanIndexedDbOptions
{
    public List<SekibanIndexedDbOption> Contexts { get; init; } = [];

    public static SekibanIndexedDbOptions Default => new();

    public SekibanIndexedDbOption GetContextOption(string context) =>
        Contexts.Find(x => x.Context == context) ?? new();

    public SekibanIndexedDbOption GetContextOption(IServiceProvider serviceProvider) =>
        Contexts.Find(x => x.Context == SekibanContextIdentifier(serviceProvider)) ?? new();

    public SekibanIndexedDbOption GetContextOption(ISekibanContext context) =>
        Contexts.Find(x => x.Context == context.SettingGroupIdentifier) ?? new();

    private static string SekibanContextIdentifier(IServiceProvider serviceProvider)
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        return sekibanContext?.SettingGroupIdentifier ?? SekibanContext.Default;
    }

    public static SekibanIndexedDbOptions FromConfiguration(IConfiguration configuration) =>
        FromConfigurationSection(
            configuration.GetSection("Sekiban"),
            configuration as IConfigurationRoot ?? throw new ConfigurationErrorsException("IndexedDB configuration failed.")
        );

    public static SekibanIndexedDbOptions FromConfigurationSection(IConfigurationSection section, IConfigurationRoot configurationRoot)
    {
        var contextSettings = new List<SekibanIndexedDbOption>();

        var defaultContextSection = section.GetSection("Default");
        if (defaultContextSection.Exists())
        {
            contextSettings.Add(SekibanIndexedDbOption.FromConfigurationSection(defaultContextSection, configurationRoot));
        }

        contextSettings.AddRange(
            section.GetSection("Contexts").GetChildren()
                .Select(context => SekibanIndexedDbOption.FromConfigurationSection(context, configurationRoot, GetLastPathComponent(context)))
        );

        if (contextSettings.Count == 0)
        {
            contextSettings.Add(SekibanIndexedDbOption.FromConfigurationSection(defaultContextSection, configurationRoot));
        }

        return new()
        {
            Contexts = contextSettings,
        };
    }

    private static string GetLastPathComponent(IConfigurationSection section) =>
        section.Path.Split(':').LastOrDefault() ?? section.Path;
}
