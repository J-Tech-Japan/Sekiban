using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Setting;
using System.Configuration;
namespace Sekiban.Infrastructure.Dynamo;

public class SekibanDynamoDbOptions
{
    public List<SekibanAwsOption> Contexts { get; init; } = new();

    public static SekibanDynamoDbOptions Default => new();

    public SekibanAwsOption GetContextOption(string context) => Contexts.Find(x => x.Context == context) ?? new SekibanAwsOption();
    public SekibanAwsOption GetContextOption(IServiceProvider serviceProvider) =>
        Contexts.Find(x => x.Context == SekibanContextIdentifier(serviceProvider)) ?? new SekibanAwsOption();
    public SekibanAwsOption GetContextOption(ISekibanContext context) =>
        Contexts.Find(x => x.Context == context.SettingGroupIdentifier) ?? new SekibanAwsOption();

    private static string SekibanContextIdentifier(IServiceProvider serviceProvider)
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        return sekibanContext?.SettingGroupIdentifier ?? SekibanContext.Default;
    }
    public static SekibanDynamoDbOptions FromConfiguration(IConfiguration configuration) =>
        FromConfigurationSection(
            configuration.GetSection("Sekiban"),
            configuration as IConfigurationRoot ?? throw new ConfigurationErrorsException("cosmos configuration failed."));
    public static SekibanDynamoDbOptions FromConfigurationSection(IConfigurationSection section, IConfigurationRoot configurationRoot)
    {
        var defaultContextSection = section.GetSection("Default");
        var contexts = section.GetSection("Contexts").GetChildren();
        var contextSettings = new List<SekibanAwsOption>();
        if (defaultContextSection.Exists())
        {
            contextSettings.Add(SekibanAwsOption.FromConfiguration(defaultContextSection, configurationRoot));
        }
        contextSettings.AddRange(
            from context in contexts
            let path = GetLastPathComponent(context)
            select SekibanAwsOption.FromConfiguration(context, configurationRoot, path));
        return new SekibanDynamoDbOptions { Contexts = contextSettings };
    }
    private static string GetLastPathComponent(IConfigurationSection section) => section.Path.Split(':').LastOrDefault() ?? section.Path;
}
