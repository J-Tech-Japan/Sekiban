namespace Sekiban.Pure.Query;

public record QueryResultGeneral(object Value, string ResultType, IQueryCommon Query) : IQueryResult
{
    public object GetValue()
    {
        return Value;
    }

    public QueryResultGeneral ToGeneral(IQueryCommon queryCommon)
    {
        return this with { Query = queryCommon };
    }
}