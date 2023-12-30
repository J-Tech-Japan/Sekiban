namespace Sekiban.Core.Setting;

/// <summary>
///     Aggregate settings using appsettings.json configuration.
/// </summary>
public class ContextAggregateSettings : AggregateSettings
{
    public ContextAggregateSettings(SekibanSettings settings, ISekibanContext sekibanContext)
    {
        var section = settings.Contexts.FirstOrDefault(m => m.Context == sekibanContext.SettingGroupIdentifier);
        Helper = section?.Aggregates ?? new AggregateSettingHelper();
    }
}
