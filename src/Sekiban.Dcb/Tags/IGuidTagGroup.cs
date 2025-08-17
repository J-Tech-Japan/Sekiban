namespace Sekiban.Dcb.Tags;

/// <summary>
///     Interface for GUID-based tag groups
///     Extends ITagGroup with a GetId() method for accessing the GUID identifier
/// </summary>
/// <typeparam name="TTagGroup">The concrete tag type</typeparam>
public interface IGuidTagGroup<TTagGroup> : ITagGroup<TTagGroup>
    where TTagGroup : IGuidTagGroup<TTagGroup>
{
    /// <summary>
    ///     Get the GUID identifier for this tag
    /// </summary>
    /// <returns>The GUID identifier</returns>
    Guid GetId();
    
    // Re-declare static abstract members to avoid CS8920
    new static abstract string TagGroupName { get; }
    new static abstract TTagGroup FromContent(string content);
}