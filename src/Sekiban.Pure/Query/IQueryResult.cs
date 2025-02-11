namespace Sekiban.Pure.Query;

public interface IQueryResult
{
    public object GetValue();
    public QueryResultGeneral ToGeneral(IQueryCommon query);
}