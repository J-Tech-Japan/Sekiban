using Microsoft.Extensions.Configuration;
using Sekiban.Core.Cache;
namespace Sekiban.Core.Setting;

public record SekibanSettings(MemoryCacheSetting MemoryCache, IEnumerable<SekibanContextSettings> Contexts)
{
    public static SekibanSettings Default => new(
        new MemoryCacheSetting(),
        new List<SekibanContextSettings> { new(new AggregateSettingHelper()) });

    public static SekibanSettings FromConfiguration(IConfiguration configuration) =>
        FromConfigurationSection(configuration.GetSection("Sekiban"));
    public static SekibanSettings FromConfigurationSection(IConfigurationSection section)
    {
        var memoryCache = section.GetValue<MemoryCacheSetting>("MemoryCache") ?? new MemoryCacheSetting();
        var defaultContextSection = section.GetSection("Default");
        var contexts = section.GetSection("Contexts").GetChildren();
        var contextSettings = new List<SekibanContextSettings>();
        if (defaultContextSection.Exists())
        {
            contextSettings.Add(
                new SekibanContextSettings(AggregateSettingHelper.FromConfigurationSection(defaultContextSection)));
        }
        contextSettings.AddRange(
            from context in contexts
            let path = GetLastPathComponent(context)
            let helper = AggregateSettingHelper.FromConfigurationSection(context)
            select new SekibanContextSettings(helper, path));
        return new SekibanSettings(memoryCache, contextSettings);
    }
    private static string GetLastPathComponent(IConfigurationSection section) =>
        section.Path.Split(':').LastOrDefault() ?? section.Path;
}
