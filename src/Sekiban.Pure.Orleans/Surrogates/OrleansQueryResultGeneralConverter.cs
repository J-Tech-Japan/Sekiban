using Sekiban.Pure.Query;
namespace Sekiban.Pure.Orleans.Surrogates;

[RegisterConverter]
public sealed class OrleansQueryResultGeneralConverter : IConverter<QueryResultGeneral, OrleansQueryResultGeneral>
{
    public QueryResultGeneral ConvertFromSurrogate(in OrleansQueryResultGeneral surrogate) =>
        surrogate.ToQueryResultGeneral();

    public OrleansQueryResultGeneral ConvertToSurrogate(in QueryResultGeneral value) =>
        OrleansQueryResultGeneral.FromQueryResultGeneral(value);
}
