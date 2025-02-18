using Sekiban.Pure.Query;
namespace Sekiban.Pure.Orleans.Surrogates;

[GenerateSerializer]
public record struct ListQueryResultSurrogate<T>(
    [property: Id(0)] int? TotalCount,
    [property: Id(1)] int? TotalPages,
    [property: Id(2)] int? CurrentPage,
    [property: Id(3)] int? PageSize,
    [property: Id(4)] IEnumerable<T> Items)
{
    public static ListQueryResult<T> ConvertFromSurrogate(ListQueryResultSurrogate<T> surrogate) =>
        new(surrogate.TotalCount, surrogate.TotalPages, surrogate.CurrentPage, surrogate.PageSize, surrogate.Items);

    public static ListQueryResultSurrogate<T> ConvertToSurrogate(ListQueryResult<T> original) =>
        new(original.TotalCount, original.TotalPages, original.CurrentPage, original.PageSize, original.Items);
}