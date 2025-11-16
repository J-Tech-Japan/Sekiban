using ResultBoxes;
using Sekiban.Dcb.Queries;
namespace Sekiban.Dcb.Tests.Queries;

/// <summary>
///     Test query that gets items by category with pagination
/// </summary>
public record GetItemsByCategoryQueryV2(string Category)
    : IMultiProjectionListQuery<TestMultiProjector, GetItemsByCategoryQueryV2, TestItem>
{
    public int? PageSize { get; init; }
    public int? PageNumber { get; init; }

    public static ResultBox<IEnumerable<TestItem>> HandleFilter(
        TestMultiProjector projector,
        GetItemsByCategoryQueryV2 query,
        IQueryContext context)
    {
        var filtered = projector.Items.Where(i => i.Category.Equals(
            query.Category,
            StringComparison.OrdinalIgnoreCase));

        return ResultBox.FromValue(filtered.AsEnumerable());
    }

    public static ResultBox<IEnumerable<TestItem>> HandleSort(
        IEnumerable<TestItem> filteredList,
        GetItemsByCategoryQueryV2 query,
        IQueryContext context)
    {
        var sorted = filteredList.OrderByDescending(i => i.Price).ThenBy(i => i.Name);

        return ResultBox.FromValue(sorted.AsEnumerable());
    }
}
