namespace Sekiban.Dcb.Queries;

/// <summary>
/// Result for list queries with pagination support
/// </summary>
/// <typeparam name="T">The type of items in the result</typeparam>
public record ListQueryResult<T>(
    int? TotalCount,
    int? TotalPages,
    int? CurrentPage,
    int? PageSize,
    IEnumerable<T> Items) where T : notnull
{
    /// <summary>
    /// Empty result
    /// </summary>
    public static ListQueryResult<T> Empty => new(0, 0, 0, 0, Array.Empty<T>());
    
    /// <summary>
    /// Create a paginated result
    /// </summary>
    public static ListQueryResult<T> CreatePaginated(
        IQueryPagingParameter pagingParam,
        List<T> allItems)
    {
        if (pagingParam.PageNumber == null || pagingParam.PageSize == null)
        {
            // Return all items without pagination
            return new ListQueryResult<T>(
                allItems.Count,
                null,
                null,
                null,
                allItems);
        }
        
        var pageNumber = pagingParam.PageNumber.Value;
        var pageSize = pagingParam.PageSize.Value;
        var totalCount = allItems.Count;
        var totalPages = (totalCount + pageSize - 1) / pageSize; // Ceiling division
        
        if (pageNumber < 1 || pageNumber > totalPages)
        {
            // Return empty result for invalid page
            return new ListQueryResult<T>(
                totalCount,
                totalPages,
                pageNumber,
                pageSize,
                Array.Empty<T>());
        }
        
        var pagedItems = allItems
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize);
        
        return new ListQueryResult<T>(
            totalCount,
            totalPages,
            pageNumber,
            pageSize,
            pagedItems);
    }
    
    public virtual bool Equals(ListQueryResult<T>? other) =>
        other != null &&
        TotalCount == other.TotalCount &&
        TotalPages == other.TotalPages &&
        CurrentPage == other.CurrentPage &&
        PageSize == other.PageSize &&
        Items.SequenceEqual(other.Items);
    
    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = TotalCount.GetHashCode();
            hashCode = hashCode * 397 ^ TotalPages.GetHashCode();
            hashCode = hashCode * 397 ^ CurrentPage.GetHashCode();
            hashCode = hashCode * 397 ^ PageSize.GetHashCode();
            hashCode = hashCode * 397 ^ Items.GetHashCode();
            return hashCode;
        }
    }
}