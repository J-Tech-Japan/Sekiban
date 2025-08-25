using Sekiban.Dcb.Queries;
namespace Sekiban.Dcb.Orleans.Surrogates;

/// <summary>
///     Orleans surrogate for ListQueryResultGeneral
/// </summary>
[GenerateSerializer]
public record struct ListQueryResultGeneralSurrogate(
    [property: Id(0)]
    int? TotalCount,
    [property: Id(1)]
    int? TotalPages,
    [property: Id(2)]
    int? CurrentPage,
    [property: Id(3)]
    int? PageSize,
    [property: Id(4)]
    IEnumerable<object> Items,
    [property: Id(5)]
    string RecordType,
    [property: Id(6)]
    IListQueryCommon Query);
