namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Query Result for List Query.
///     Query result for the paging query can use generic type to specify the type of the item.
/// </summary>
/// <param name="TotalCount"></param>
/// <param name="TotalPages"></param>
/// <param name="CurrentPage"></param>
/// <param name="PageSize"></param>
/// <param name="Items"></param>
/// <typeparam name="T"></typeparam>
public record ListQueryResult<T>(int? TotalCount, int? TotalPages, int? CurrentPage, int? PageSize, IEnumerable<T> Items);
