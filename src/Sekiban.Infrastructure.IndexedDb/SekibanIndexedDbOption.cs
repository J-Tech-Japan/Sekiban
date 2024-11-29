using Microsoft.Extensions.Configuration;
using Sekiban.Core.Setting;

namespace Sekiban.Infrastructure.IndexedDb;

public class SekibanIndexedDbOption
{
    public const string DatabaseNameDefaultValue = "sekiban";

    // public const string EventsStoreName = "events";
    // public const string EventsStoreDissolvableName = "dissolvable-events";
    // public const string CommandStoreName = "commands";
    // public const string SingleProjectionSnapshotStoreName = "single-projection-snapshots";
    // public const string MultiProjectionSnapshotStoreName = "multi-projection-snapshots";

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
