namespace Sekiban.Core.Setting;

/// <summary>
///     Sekiban Container Context Interface.
///     Getting this from DI makes it identify which Sekiban Container is used.
/// </summary>
public interface ISekibanContext
{
    /// <summary>
    ///     Setting Group Identifier.
    /// </summary>
    public string SettingGroupIdentifier { get; }
    /// <summary>
    ///     Run Sekiban Action with Setting Group Identifier and returns specific type.
    /// </summary>
    /// <param name="sekibanIdentifier"></param>
    /// <param name="action"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public Task<T> SekibanActionAsync<T>(string sekibanIdentifier, Func<Task<T>> action);
    /// <summary>
    ///     Run Sekiban Action with Setting Group Identifier.
    /// </summary>
    /// <param name="sekibanIdentifier"></param>
    /// <param name="action"></param>
    /// <returns></returns>
    public Task SekibanActionAsync(string sekibanIdentifier, Func<Task> action);
}
