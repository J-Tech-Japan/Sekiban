namespace Sekiban.Core.Cache;

public record MemoryCacheSettingsSection(int AbsoluteExpirationMinutes = 120, int SlidingExpirationMinutes = 15)
{
}
