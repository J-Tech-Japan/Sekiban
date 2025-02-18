using Sekiban.Pure.Query;
namespace Sekiban.Pure.Orleans.Surrogates;

[GenerateSerializer]
public record struct OrleansQueryResultGeneral(
    [property: Id(0)] object Value,
    [property: Id(1)] string ResultType,
    [property: Id(2)] IQueryCommon Query) : IOrleansQueryResult
{
    public static OrleansQueryResultGeneral FromQueryResultGeneral(QueryResultGeneral queryResultGeneral) =>
        new(
            queryResultGeneral.Value,
            queryResultGeneral.ResultType,
            queryResultGeneral.Query);

    public QueryResultGeneral ToQueryResultGeneral() => new(Value, ResultType, Query);
}