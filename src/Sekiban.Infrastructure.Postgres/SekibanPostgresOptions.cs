using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Setting;
using System.Configuration;
namespace Sekiban.Infrastructure.Postgres;

public record SekibanPostgresOptions
{

    public List<SekibanPostgresDbOption> Contexts { get; init; } = [];

    public static SekibanPostgresOptions Default => new();

    public SekibanPostgresDbOption GetContextOption(string context) =>
        Contexts.Find(x => x.Context == context) ?? new SekibanPostgresDbOption();
    public SekibanPostgresDbOption GetContextOption(IServiceProvider serviceProvider) =>
        Contexts.Find(x => x.Context == SekibanContextIdentifier(serviceProvider)) ?? new SekibanPostgresDbOption();
    public SekibanPostgresDbOption GetContextOption(ISekibanContext context) =>
        Contexts.Find(x => x.Context == context.SettingGroupIdentifier) ?? new SekibanPostgresDbOption();

    private static string SekibanContextIdentifier(IServiceProvider serviceProvider)
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        return sekibanContext?.SettingGroupIdentifier ?? SekibanContext.Default;
    }
    public static SekibanPostgresOptions FromConfiguration(IConfiguration configuration) =>
        FromConfigurationSection(
            configuration.GetSection("Sekiban"),
            configuration as IConfigurationRoot ??
            throw new ConfigurationErrorsException("postgres configuration failed."));
    public static SekibanPostgresOptions FromConfigurationSection(
        IConfigurationSection section,
        IConfigurationRoot configurationRoot)
    {
        var defaultContextSection = section.GetSection("Default");
        var contexts = section.GetSection("Contexts").GetChildren().ToList();
        var contextSettings = new List<SekibanPostgresDbOption>();
        if (defaultContextSection.Exists())
        {
            contextSettings.Add(SekibanPostgresDbOption.FromConfiguration(defaultContextSection, configurationRoot));
        }
        contextSettings.AddRange(
            from context in contexts
            let path = GetLastPathComponent(context)
            select SekibanPostgresDbOption.FromConfiguration(context, configurationRoot, path));
        if (!defaultContextSection.Exists() && contexts.Count == 0)
        {
            contextSettings.Add(SekibanPostgresDbOption.FromConfiguration(defaultContextSection, configurationRoot));
        }
        return new SekibanPostgresOptions { Contexts = contextSettings };
    }
    public static SekibanPostgresOptions FromConnectionStringName(
        string connectionStringName,
        IConfigurationRoot configurationRoot) =>
        new()
        {
            Contexts = new List<SekibanPostgresDbOption>
            {
                SekibanPostgresDbOption.FromConnectionStringName(connectionStringName, configurationRoot)
            }
        };
    private static string GetLastPathComponent(IConfigurationSection section) =>
        section.Path.Split(':').LastOrDefault() ?? section.Path;
}
