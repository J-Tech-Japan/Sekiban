namespace Sekiban.Core.Query.QueryModel;

public record QueryListResult<T>(int? TotalCount, int? TotalPages, int? CurrentPage, int? PageSize, IEnumerable<T> Items);
