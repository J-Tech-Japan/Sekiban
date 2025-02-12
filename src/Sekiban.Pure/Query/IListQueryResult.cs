namespace Sekiban.Pure.Query;

public interface IListQueryResult
{
    public ListQueryResultGeneral ToGeneral(IListQueryCommon query);
}