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
public record ListQueryResult<T>(
    int? TotalCount,
    int? TotalPages,
    int? CurrentPage,
    int? PageSize,
    IEnumerable<T> Items)
{
    public static ListQueryResult<T> Empty => new(0, 0, 0, 0, Array.Empty<T>());

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
            hashCode = (hashCode * 397) ^ TotalPages.GetHashCode();
            hashCode = (hashCode * 397) ^ CurrentPage.GetHashCode();
            hashCode = (hashCode * 397) ^ PageSize.GetHashCode();
            hashCode = (hashCode * 397) ^ Items.GetHashCode();
            return hashCode;
        }
    }
}