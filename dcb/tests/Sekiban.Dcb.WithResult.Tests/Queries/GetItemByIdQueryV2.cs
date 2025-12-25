using ResultBoxes;
using Sekiban.Dcb.Queries;
namespace Sekiban.Dcb.Tests.Queries;

/// <summary>
///     Test query that gets a single item by ID
/// </summary>
public record GetItemByIdQueryV2(Guid Id) : IMultiProjectionQuery<TestMultiProjector, GetItemByIdQueryV2, TestItem>
{
    public static ResultBox<TestItem> HandleQuery(
        TestMultiProjector projector,
        GetItemByIdQueryV2 query,
        IQueryContext context)
    {
        var item = projector.Items.FirstOrDefault(i => i.Id == query.Id);
        if (item == null)
        {
            return ResultBox.Error<TestItem>(new InvalidOperationException($"Item with ID {query.Id} not found"));
        }
        return ResultBox.FromValue(item);
    }
}
