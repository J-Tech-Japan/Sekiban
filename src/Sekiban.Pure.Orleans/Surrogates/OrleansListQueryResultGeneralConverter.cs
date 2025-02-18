using Sekiban.Pure.Query;
namespace Sekiban.Pure.Orleans.Surrogates;

[RegisterConverter]
public sealed class
    OrleansListQueryResultGeneralConverter : IConverter<ListQueryResultGeneral, OrleansListQueryResultGeneral>
{
    public ListQueryResultGeneral ConvertFromSurrogate(in OrleansListQueryResultGeneral surrogate) =>
        new(
            surrogate.TotalCount,
            surrogate.TotalPages,
            surrogate.CurrentPage,
            surrogate.PageSize,
            surrogate.Items,
            surrogate.RecordType,
            surrogate.Query);

    public OrleansListQueryResultGeneral ConvertToSurrogate(in ListQueryResultGeneral value) =>
        new(
            value.TotalCount,
            value.TotalPages,
            value.CurrentPage,
            value.PageSize,
            value.Items,
            value.RecordType,
            value.Query);
}