using ResultBoxes;
using Sekiban.Dcb.Queries;

namespace Sekiban.Dcb.Tests.Queries;

/// <summary>
/// Test query that gets a single item by ID
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

/// <summary>
/// Test query that gets items by category with pagination
/// </summary>
public record GetItemsByCategoryQueryV2(string Category) : IMultiProjectionListQuery<TestMultiProjector, GetItemsByCategoryQueryV2, TestItem>
{
    public int? PageSize { get; init; }
    public int? PageNumber { get; init; }
    
    public static ResultBox<IEnumerable<TestItem>> HandleFilter(
        TestMultiProjector projector,
        GetItemsByCategoryQueryV2 query,
        IQueryContext context)
    {
        var filtered = projector.Items
            .Where(i => i.Category.Equals(query.Category, StringComparison.OrdinalIgnoreCase));
        
        return ResultBox.FromValue(filtered.AsEnumerable());
    }
    
    public static ResultBox<IEnumerable<TestItem>> HandleSort(
        IEnumerable<TestItem> filteredList,
        GetItemsByCategoryQueryV2 query,
        IQueryContext context)
    {
        var sorted = filteredList
            .OrderByDescending(i => i.Price)
            .ThenBy(i => i.Name);
        
        return ResultBox.FromValue(sorted.AsEnumerable());
    }
}

/// <summary>
/// Test query that gets all items sorted by creation date
/// </summary>
public record GetAllItemsQueryV2 : IMultiProjectionListQuery<TestMultiProjector, GetAllItemsQueryV2, TestItem>
{
    public int? PageSize { get; init; }
    public int? PageNumber { get; init; }
    
    public static ResultBox<IEnumerable<TestItem>> HandleFilter(
        TestMultiProjector projector,
        GetAllItemsQueryV2 query,
        IQueryContext context)
    {
        return ResultBox.FromValue(projector.Items.AsEnumerable());
    }
    
    public static ResultBox<IEnumerable<TestItem>> HandleSort(
        IEnumerable<TestItem> filteredList,
        GetAllItemsQueryV2 query,
        IQueryContext context)
    {
        var sorted = filteredList.OrderBy(i => i.CreatedAt);
        return ResultBox.FromValue(sorted.AsEnumerable());
    }
}

/// <summary>
/// Test query that gets item count by category
/// </summary>
public record GetItemCountByCategoryQueryV2(string Category) : IMultiProjectionQuery<TestMultiProjector, GetItemCountByCategoryQueryV2, int>
{
    public static ResultBox<int> HandleQuery(
        TestMultiProjector projector,
        GetItemCountByCategoryQueryV2 query,
        IQueryContext context)
    {
        var count = projector.Items.Count(i => 
            i.Category.Equals(query.Category, StringComparison.OrdinalIgnoreCase));
        return ResultBox.FromValue(count);
    }
}