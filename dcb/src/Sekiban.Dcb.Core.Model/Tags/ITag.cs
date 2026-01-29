namespace Sekiban.Dcb.Tags;

public interface ITag
{
    bool IsConsistencyTag();
    string GetTagGroup();
    string GetTagContent();

    /// <summary>
    ///     Gets the full tag string in format "TagGroup:TagContent"
    /// </summary>
    string GetTag() => $"{GetTagGroup()}:{GetTagContent()}";
}
