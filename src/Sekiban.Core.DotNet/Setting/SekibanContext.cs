namespace Sekiban.Core.Setting;

/// <summary>
///     Sekiban context implementation.
/// </summary>
public class SekibanContext : ISekibanContext
{
    public const string Default = "Default";

    public string SettingGroupIdentifier { get; set; } = Default;

    public async Task<T> SekibanActionAsync<T>(string sekibanIdentifier, Func<Task<T>> action)
    {
        var identifierToRestore = SettingGroupIdentifier;
        SettingGroupIdentifier = sekibanIdentifier;
        var returnValue = await action();
        SettingGroupIdentifier = identifierToRestore;
        return returnValue;
    }

    public async Task SekibanActionAsync(string sekibanIdentifier, Func<Task> action)
    {
        var identifierToRestore = SettingGroupIdentifier;
        SettingGroupIdentifier = sekibanIdentifier;
        await action();
        SettingGroupIdentifier = identifierToRestore;
    }
}
