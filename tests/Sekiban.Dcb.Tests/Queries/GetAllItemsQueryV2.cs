using ResultBoxes;
using Sekiban.Dcb.Queries;
namespace Sekiban.Dcb.Tests.Queries;

/// <summary>
///     Test query that gets all items sorted by creation date
/// </summary>
public record GetAllItemsQueryV2 : IMultiProjectionListQuery<TestMultiProjector, GetAllItemsQueryV2, TestItem>
{
    public int? PageSize { get; init; }
    public int? PageNumber { get; init; }

    public static ResultBox<IEnumerable<TestItem>> HandleFilter(
        TestMultiProjector projector,
        GetAllItemsQueryV2 query,
        IQueryContext context) =>
        ResultBox.FromValue(projector.Items.AsEnumerable());

    public static ResultBox<IEnumerable<TestItem>> HandleSort(
        IEnumerable<TestItem> filteredList,
        GetAllItemsQueryV2 query,
        IQueryContext context)
    {
        var sorted = filteredList.OrderBy(i => i.CreatedAt);
        return ResultBox.FromValue(sorted.AsEnumerable());
    }
}
