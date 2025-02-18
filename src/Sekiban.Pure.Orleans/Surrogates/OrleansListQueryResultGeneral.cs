using Sekiban.Pure.Query;
namespace Sekiban.Pure.Orleans.Surrogates;

[GenerateSerializer]
public record struct OrleansListQueryResultGeneral(
    [property: Id(0)] int? TotalCount,
    [property: Id(1)] int? TotalPages,
    [property: Id(2)] int? CurrentPage,
    [property: Id(3)] int? PageSize,
    [property: Id(4)] IEnumerable<object> Items,
    [property: Id(5)] string RecordType,
    [property: Id(6)] IListQueryCommon Query)
{
}