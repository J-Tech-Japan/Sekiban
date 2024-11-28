using System.Configuration;
using Microsoft.Extensions.Configuration;

namespace Sekiban.Infrastructure.IndexedDb;

public class SekibanIndexedDbOptions
{
    public static SekibanIndexedDbOptions FromConfiguration(IConfiguration configuration) =>
        FromConfigurationSection(
            configuration.GetSection("Sekiban"),
            configuration as IConfigurationRoot ?? throw new ConfigurationErrorsException("IndexedDB configuration failed.")
        );

    public static SekibanIndexedDbOptions FromConfigurationSection(IConfigurationSection configurationSection, IConfigurationRoot configurationRoot)
    {
        // TODO
        return new();
    }
}
