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

    /// <summary>
    ///     Indicates whether the projection is still catching up from the event store.
    ///     When true, the results may be incomplete or stale.
    /// </summary>
    bool IsCatchUpInProgress => false;
}
