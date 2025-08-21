using Orleans;
using Sekiban.Dcb.Queries;

namespace Sekiban.Dcb.Orleans.Surrogates;

/// <summary>
/// Orleans surrogate converter for QueryResultGeneral
/// </summary>
[RegisterConverter]
public sealed class QueryResultGeneralSurrogateConverter : IConverter<QueryResultGeneral, QueryResultGeneralSurrogate>
{
    public QueryResultGeneral ConvertFromSurrogate(in QueryResultGeneralSurrogate surrogate) =>
        new(surrogate.Value, surrogate.ResultType, surrogate.Query);

    public QueryResultGeneralSurrogate ConvertToSurrogate(in QueryResultGeneral value) =>
        new(value.Value, value.ResultType, value.Query);
}
