namespace Sekiban.Dcb.Tags;

public interface IStringTagGroup<TTagGroup> : ITagGroup<TTagGroup> where TTagGroup : IStringTagGroup<TTagGroup>
{
    /// <summary>
    ///     Get the string identifier for this tag
    /// </summary>
    /// <returns>The string identifier</returns>
    string GetId();
    string ITag.GetTagContent() => GetId();
}