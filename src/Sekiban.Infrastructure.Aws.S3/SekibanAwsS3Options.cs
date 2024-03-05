using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Setting;
using System.Configuration;
namespace Sekiban.Infrastructure.Aws.S3;

public class SekibanAwsS3Options
{
    public List<SekibanAwsS3Option> Contexts { get; init; } = [];

    public static SekibanAwsS3Options Default => new();

    public SekibanAwsS3Option GetContextOption(string context) => Contexts.Find(x => x.Context == context) ?? new SekibanAwsS3Option();
    public SekibanAwsS3Option GetContextOption(IServiceProvider serviceProvider) =>
        Contexts.Find(x => x.Context == SekibanContextIdentifier(serviceProvider)) ?? new SekibanAwsS3Option();
    public SekibanAwsS3Option GetContextOption(ISekibanContext context) =>
        Contexts.Find(x => x.Context == context.SettingGroupIdentifier) ?? new SekibanAwsS3Option();

    private static string SekibanContextIdentifier(IServiceProvider serviceProvider)
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        return sekibanContext?.SettingGroupIdentifier ?? SekibanContext.Default;
    }
    public static SekibanAwsS3Options FromConfiguration(IConfiguration configuration) =>
        FromConfigurationSection(
            configuration.GetSection("Sekiban"),
            configuration as IConfigurationRoot ?? throw new ConfigurationErrorsException("cosmos configuration failed."));
    public static SekibanAwsS3Options FromConfigurationSection(IConfigurationSection section, IConfigurationRoot configurationRoot)
    {
        var defaultContextSection = section.GetSection("Default");
        var contexts = section.GetSection("Contexts").GetChildren().ToList();
        var contextSettings = new List<SekibanAwsS3Option>();
        if (defaultContextSection.Exists())
        {
            contextSettings.Add(SekibanAwsS3Option.FromConfiguration(defaultContextSection, configurationRoot));
        }
        contextSettings.AddRange(
            from context in contexts
            let path = GetLastPathComponent(context)
            select SekibanAwsS3Option.FromConfiguration(context, configurationRoot, path));
        if (!defaultContextSection.Exists() && contexts.Count == 0)
        {
            contextSettings.Add(SekibanAwsS3Option.FromConfiguration(defaultContextSection, configurationRoot));
        }
        return new SekibanAwsS3Options { Contexts = contextSettings };
    }
    private static string GetLastPathComponent(IConfigurationSection section) => section.Path.Split(':').LastOrDefault() ?? section.Path;
}
