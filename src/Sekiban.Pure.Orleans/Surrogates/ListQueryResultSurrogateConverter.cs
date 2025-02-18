using Sekiban.Pure.Query;
namespace Sekiban.Pure.Orleans.Surrogates;

[RegisterConverter]
public sealed class ListQueryResultSurrogateConverter<T> : IConverter<ListQueryResult<T>, ListQueryResultSurrogate<T>>
{
    public ListQueryResult<T> ConvertFromSurrogate(in ListQueryResultSurrogate<T> surrogate) =>
        ListQueryResultSurrogate<T>.ConvertFromSurrogate(surrogate);

    public ListQueryResultSurrogate<T> ConvertToSurrogate(in ListQueryResult<T> original) =>
        ListQueryResultSurrogate<T>.ConvertToSurrogate(original);
}