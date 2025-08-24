using ResultBoxes;
using Sekiban.Dcb.Queries;
namespace Sekiban.Dcb.Tests.Queries;

/// <summary>
///     Test query that gets item count by category
/// </summary>
public record GetItemCountByCategoryQueryV2(string Category)
    : IMultiProjectionQuery<TestMultiProjector, GetItemCountByCategoryQueryV2, int>
{
    public static ResultBox<int> HandleQuery(
        TestMultiProjector projector,
        GetItemCountByCategoryQueryV2 query,
        IQueryContext context)
    {
        var count = projector.Items.Count(i => i.Category.Equals(query.Category, StringComparison.OrdinalIgnoreCase));
        return ResultBox.FromValue(count);
    }
}
