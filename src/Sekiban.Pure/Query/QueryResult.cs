namespace Sekiban.Pure.Query;

public record QueryResult<T>(T Value) : IQueryResult
{
    public object GetValue()
    {
        return Value;
    }

    public QueryResultGeneral ToGeneral(IQueryCommon query)
    {
        return new QueryResultGeneral(Value, typeof(T).Name, query);
    }
}