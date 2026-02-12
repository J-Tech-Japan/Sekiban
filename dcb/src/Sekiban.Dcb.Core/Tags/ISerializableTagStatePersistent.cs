namespace Sekiban.Dcb.Tags;

/// <summary>
///     Optional extension for ITagStatePersistent that can store/load SerializableTagState directly.
///     Implementations can use this to avoid unnecessary typed TagState round-trips internally.
/// </summary>
public interface ISerializableTagStatePersistent
{
    /// <summary>
    ///     Loads cached serializable tag state.
    /// </summary>
    Task<SerializableTagState?> LoadSerializableStateAsync();

    /// <summary>
    ///     Saves serializable tag state.
    /// </summary>
    Task SaveSerializableStateAsync(SerializableTagState state);
}
