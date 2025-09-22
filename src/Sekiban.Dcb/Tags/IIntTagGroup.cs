namespace Sekiban.Dcb.Tags;

public interface IIntTagGroup<TTagGroup> : ITagGroup<TTagGroup> where TTagGroup : IIntTagGroup<TTagGroup>
{

    // Re-declare static abstract members to avoid CS8920
    static abstract new string TagGroupName { get; }
    /// <summary>
    ///     Get the GUID identifier for this tag
    /// </summary>
    /// <returns>The GUID identifier</returns>
    int GetId();
}
