namespace Sekiban.EventSourcing.Queries.QueryModels;

public record QueryFilterListResult<T>(int? TotalCount, int? TotalPages, int? CurrentPage, int?PageSize, IEnumerable<T> Items);
