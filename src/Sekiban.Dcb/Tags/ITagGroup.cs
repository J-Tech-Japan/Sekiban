namespace Sekiban.Dcb.Tags;

public interface ITagGroup<TTagGroup> : ITag where TTagGroup : ITagGroup<TTagGroup>
{
    static abstract string TagGroupName { get; }
    string ITag.GetTagGroup() => TTagGroup.TagGroupName;
    static abstract TTagGroup FromContent(string content);
}
