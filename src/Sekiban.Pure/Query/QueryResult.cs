namespace Sekiban.Pure.Query;

public record QueryResult<T>(T Value) : IQueryResult 
    where T : notnull
{
    public object GetValue() => Value;

    public QueryResultGeneral ToGeneral(IQueryCommon query) => new(Value, typeof(T).Name, query);
}
