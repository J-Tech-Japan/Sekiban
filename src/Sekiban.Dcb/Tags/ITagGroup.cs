namespace Sekiban.Dcb.Tags;

public interface ITagGroup<TTagGroup> :ITag where TTagGroup : ITagGroup<TTagGroup>
{
    static abstract string GetTagGroupName();
    string ITag.GetTagGroup() => TTagGroup.GetTagGroupName();
    static abstract TTagGroup FromContent(string content);
}