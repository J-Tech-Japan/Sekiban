namespace Sekiban.Pure.Query;

public record QueryResultGeneral(object Value, string ResultType, IQueryCommon Query) : IQueryResult
{
    public object GetValue() => Value;

    public QueryResultGeneral ToGeneral(IQueryCommon queryCommon) => this with { Query = queryCommon };
}
