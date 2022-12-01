namespace Sekiban.Core.Query.QueryModel;

public record ListQueryResult<T>(int? TotalCount, int? TotalPages, int? CurrentPage, int? PageSize,
    IEnumerable<T> Items);
