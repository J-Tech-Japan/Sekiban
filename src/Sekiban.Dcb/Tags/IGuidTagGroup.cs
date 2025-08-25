namespace Sekiban.Dcb.Tags;

/// <summary>
///     Interface for GUID-based tag groups
///     Extends ITagGroup with a GetId() method for accessing the GUID identifier
/// </summary>
/// <typeparam name="TTagGroup">The concrete tag type</typeparam>
public interface IGuidTagGroup<TTagGroup> : ITagGroup<TTagGroup> where TTagGroup : IGuidTagGroup<TTagGroup>
{

    // Re-declare static abstract members to avoid CS8920
    static abstract new string TagGroupName { get; }
    /// <summary>
    ///     Get the GUID identifier for this tag
    /// </summary>
    /// <returns>The GUID identifier</returns>
    Guid GetId();
    static abstract new TTagGroup FromContent(string content);
}
