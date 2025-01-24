using Microsoft.Extensions.Configuration;
using Sekiban.Core.Setting;

namespace Sekiban.Infrastructure.IndexedDb;

public class SekibanIndexedDbOption
{
    public const string DatabaseNameDefaultValue = "sekiban";

    public string Context { get; init; } = SekibanContext.Default;
    public string DatabaseName { get; init; } = DatabaseNameDefaultValue;

    public static SekibanIndexedDbOption FromConfigurationSection(
        IConfigurationSection section,
        IConfigurationRoot configurationRoot,
        string context = SekibanContext.Default
    )
    {
        var indexedDbSection = section.GetSection("IndexedDb");

        var databaseName = indexedDbSection.GetValue<string>(nameof(DatabaseName)) ?? DatabaseNameDefaultValue;

        return new()
        {
            Context = context,
            DatabaseName = databaseName,
        };
    }
}
