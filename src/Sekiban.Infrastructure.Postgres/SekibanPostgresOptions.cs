using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Setting;
using System.Configuration;
namespace Sekiban.Infrastructure.Postgres;

public record SekibanPostgresOptions
{

    public const string PostgresBlobTypeS3 = "s3";
    public const string PostgresBlobTypeAzureBlobStorage = "azureBlobStorage";
    public List<SekibanPostgresDbOption> Contexts { get; init; } = [];
    public string BlobType { get; init; } = PostgresBlobTypeAzureBlobStorage;

    public static SekibanPostgresOptions Default => new();

    public SekibanPostgresDbOption GetContextOption(string context) => Contexts.Find(x => x.Context == context) ?? new SekibanPostgresDbOption();
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
            configuration as IConfigurationRoot ?? throw new ConfigurationErrorsException("postgres configuration failed."));
    public static SekibanPostgresOptions FromConfigurationSection(IConfigurationSection section, IConfigurationRoot configurationRoot)
    {
        var defaultContextSection = section.GetSection("Default");
        var contexts = section.GetSection("Contexts").GetChildren();
        var contextSettings = new List<SekibanPostgresDbOption>();
        if (defaultContextSection.Exists())
        {
            contextSettings.Add(SekibanPostgresDbOption.FromConfiguration(defaultContextSection, configurationRoot));
        }
        contextSettings.AddRange(
            from context in contexts
            let path = GetLastPathComponent(context)
            select SekibanPostgresDbOption.FromConfiguration(context, configurationRoot, path));
        var blobType = section.GetValue<string>(nameof(BlobType)) ?? PostgresBlobTypeAzureBlobStorage;
        return new SekibanPostgresOptions { Contexts = contextSettings, BlobType = blobType };
    }
    private static string GetLastPathComponent(IConfigurationSection section) => section.Path.Split(':').LastOrDefault() ?? section.Path;
}
