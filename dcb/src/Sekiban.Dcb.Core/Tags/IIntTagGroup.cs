namespace Sekiban.Dcb.Tags;

public interface IIntTagGroup<TTagGroup> : ITagGroup<TTagGroup> where TTagGroup : IIntTagGroup<TTagGroup>
{

    /// <summary>
    ///     Get the GUID identifier for this tag
    /// </summary>
    /// <returns>The GUID identifier</returns>
    int GetId();
    string ITag.GetTagContent() => GetId().ToString("D");
}
