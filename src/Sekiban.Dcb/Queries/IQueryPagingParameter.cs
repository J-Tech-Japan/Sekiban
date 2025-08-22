using System.ComponentModel.DataAnnotations;

namespace Sekiban.Dcb.Queries;

/// <summary>
/// Interface for queries with paging parameters
/// </summary>
public interface IQueryPagingParameter
{
    /// <summary>
    /// The page size for pagination
    /// </summary>
    [Range(1, int.MaxValue)]
    int? PageSize { get; }
    
    /// <summary>
    /// The page number for pagination (1-based)
    /// </summary>
    [Range(1, int.MaxValue)]
    int? PageNumber { get; }
}