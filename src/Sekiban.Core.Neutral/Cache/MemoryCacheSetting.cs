namespace Sekiban.Core.Cache;

/// <summary>
///     Set memory cache setting.
///     Can be used with appsettings.json and set value manually as well.
/// </summary>
public class MemoryCacheSetting
{
    public MemoryCacheSettingsSection SingleProjection { get; init; } = new();
    public MemoryCacheSettingsSection Snapshot { get; init; } = new();
    public MemoryCacheSettingsSection MultiProjection { get; init; } = new();
}
