namespace DcbLib.Tags;

public interface ITag
{
    bool IsConsistencyTag();
    string GetTagGroup();
    string GetTag();
}