using Microsoft.Extensions.Configuration;
namespace Sekiban.Infrastructure.Cosmos;

public class SekibanCosmosDbOptions
{
    public List<SekibanCosmosDbOption> Contexts { get; init; } = new();

    public static SekibanCosmosDbOptions Default => new();

    public static SekibanCosmosDbOptions FromConfiguration(IConfiguration configuration) =>
        FromConfigurationSection(configuration.GetSection("Sekiban"));
    public static SekibanCosmosDbOptions FromConfigurationSection(IConfigurationSection section)
    {
        var defaultContextSection = section.GetSection("Default");
        var contexts = section.GetSection("Contexts").GetChildren();
        var contextSettings = new List<SekibanCosmosDbOption>();
        if (defaultContextSection.Exists())
        {
            contextSettings.Add(SekibanCosmosDbOption.FromConfiguration(defaultContextSection));
        }
        contextSettings.AddRange(
            from context in contexts let path = GetLastPathComponent(context) select SekibanCosmosDbOption.FromConfiguration(context, path));
        return new SekibanCosmosDbOptions { Contexts = contextSettings };
    }
    private static string GetLastPathComponent(IConfigurationSection section) => section.Path.Split(':').LastOrDefault() ?? section.Path;
}
