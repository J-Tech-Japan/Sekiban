namespace Sekiban.Dcb.Tags;

public interface ITagCommon
{
    bool IsConsistencyTag();
    string GetTagGroup();
    string GetTagContent();
}