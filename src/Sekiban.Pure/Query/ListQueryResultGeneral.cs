namespace Sekiban.Pure.Query;

public record ListQueryResultGeneral(
    int? TotalCount,
    int? TotalPages,
    int? CurrentPage,
    int? PageSize,
    IEnumerable<object> Items,
    string RecordType,
    IListQueryCommon Query) : IListQueryResult
{
    public static ListQueryResultGeneral Empty =>
        new(0, 0, 0, 0, Array.Empty<object>(), string.Empty, new EmptyListQueryCommon());

    public virtual bool Equals(ListQueryResultGeneral? other)
    {
        return other != null &&
               TotalCount == other.TotalCount &&
               TotalPages == other.TotalPages &&
               CurrentPage == other.CurrentPage &&
               PageSize == other.PageSize &&
               Items.SequenceEqual(other.Items);
    }

    public ListQueryResultGeneral ToGeneral(IListQueryCommon query)
    {
        return this;
    }

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