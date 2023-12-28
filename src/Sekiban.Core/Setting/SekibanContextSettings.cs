namespace Sekiban.Core.Setting;

public record SekibanContextSettings(AggregateSettingHelper Aggregates, string Context = SekibanContext.Default)
{
}