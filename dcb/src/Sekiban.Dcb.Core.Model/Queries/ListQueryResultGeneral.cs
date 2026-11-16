namespace Sekiban.Dcb.Queries;

/// <summary>
///     General list query result wrapper for Orleans serialization
/// </summary>
public record ListQueryResultGeneral(
    int? TotalCount,
    int? TotalPages,
    int? CurrentPage,
    int? PageSize,
    IEnumerable<object> Items,
    string RecordType,
    IListQueryCommon Query,
    bool IsCatchUpInProgress = false) : IListQueryResult
{
    public static ListQueryResultGeneral Empty =>
        new(0, 0, 0, 0, Array.Empty<object>(), string.Empty, new EmptyListQueryCommon());

    public virtual bool Equals(ListQueryResultGeneral? other) =>
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

    /// <summary>
    ///     Converts the items to <typeparamref name="T" />.
    /// </summary>
    /// <exception cref="InvalidCastException">Thrown when an item is not a <typeparamref name="T" />.</exception>
    public ListQueryResult<T> ToTypedResult<T>() where T : notnull
    {
        var typedItems = Items.Cast<T>().ToList();
        return new ListQueryResult<T>(TotalCount, TotalPages, CurrentPage, PageSize, typedItems, IsCatchUpInProgress);
    }
}
