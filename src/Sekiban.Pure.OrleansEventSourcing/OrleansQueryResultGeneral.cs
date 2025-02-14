using Sekiban.Pure.Query;

namespace Sekiban.Pure.OrleansEventSourcing;

[GenerateSerializer]
public record OrleansQueryResultGeneral(
    [property: Id(0)] object Value,
    [property: Id(1)] string ResultType,
    [property: Id(2)] IQueryCommon Query) : IOrleansQueryResult
{
    public static OrleansQueryResultGeneral FromQueryResultGeneral(QueryResultGeneral queryResultGeneral)
    {
        return new OrleansQueryResultGeneral(queryResultGeneral.Value, queryResultGeneral.ResultType,
            queryResultGeneral.Query);
    }

    public QueryResultGeneral ToQueryResultGeneral()
    {
        return new QueryResultGeneral(Value, ResultType, Query);
    }
}