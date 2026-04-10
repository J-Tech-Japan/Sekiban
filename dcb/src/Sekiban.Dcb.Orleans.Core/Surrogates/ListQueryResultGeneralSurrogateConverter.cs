using Sekiban.Dcb.Queries;
namespace Sekiban.Dcb.Orleans.Surrogates;

/// <summary>
///     Orleans surrogate converter for ListQueryResultGeneral
/// </summary>
[RegisterConverter]
public sealed class
    ListQueryResultGeneralSurrogateConverter : IConverter<ListQueryResultGeneral, ListQueryResultGeneralSurrogate>
{
    public ListQueryResultGeneral ConvertFromSurrogate(in ListQueryResultGeneralSurrogate surrogate) =>
        new(
            surrogate.TotalCount,
            surrogate.TotalPages,
            surrogate.CurrentPage,
            surrogate.PageSize,
            surrogate.Items,
            surrogate.RecordType,
            surrogate.Query,
            surrogate.IsCatchUpInProgress);

    public ListQueryResultGeneralSurrogate ConvertToSurrogate(in ListQueryResultGeneral value) =>
        new(
            value.TotalCount,
            value.TotalPages,
            value.CurrentPage,
            value.PageSize,
            value.Items,
            value.RecordType,
            value.Query,
            value.IsCatchUpInProgress);
}
