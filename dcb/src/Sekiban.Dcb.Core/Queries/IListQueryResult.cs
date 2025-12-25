namespace Sekiban.Dcb.Queries;

/// <summary>
///     Base interface for list query results
/// </summary>
public interface IListQueryResult
{
    int? TotalCount { get; }
    int? TotalPages { get; }
    int? CurrentPage { get; }
    int? PageSize { get; }
}
