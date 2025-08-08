namespace Sekiban.Dcb.Tags;

public interface ITag
{
    bool IsConsistencyTag();
    string GetTagGroup();
    string GetTagContent();
}