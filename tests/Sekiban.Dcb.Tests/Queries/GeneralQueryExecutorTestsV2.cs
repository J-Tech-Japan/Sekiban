using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Tests.Queries;

public class GeneralQueryExecutorTestsV2
{
    private readonly IServiceProvider _serviceProvider;
    private readonly GeneralQueryExecutor _queryExecutor;
    
    public GeneralQueryExecutorTestsV2()
    {
        var services = new ServiceCollection();
        services.AddSingleton<GeneralQueryExecutor>();
        _serviceProvider = services.BuildServiceProvider();
        _queryExecutor = new GeneralQueryExecutor(_serviceProvider);
    }
    
    private TestMultiProjector CreateSampleProjector()
    {
        var projector = TestMultiProjector.GenerateInitialPayload();
        var events = new List<(Event, List<ITag>)>
        {
            (CreateEvent(new ItemAdded(Guid.NewGuid(), "Item 1", "Electronics", 100m, DateTime.UtcNow.AddDays(-5))), new List<ITag>()),
            (CreateEvent(new ItemAdded(Guid.NewGuid(), "Item 2", "Books", 25m, DateTime.UtcNow.AddDays(-4))), new List<ITag>()),
            (CreateEvent(new ItemAdded(Guid.NewGuid(), "Item 3", "Electronics", 250m, DateTime.UtcNow.AddDays(-3))), new List<ITag>()),
            (CreateEvent(new ItemAdded(Guid.NewGuid(), "Item 4", "Books", 15m, DateTime.UtcNow.AddDays(-2))), new List<ITag>()),
            (CreateEvent(new ItemAdded(Guid.NewGuid(), "Item 5", "Clothing", 50m, DateTime.UtcNow.AddDays(-1))), new List<ITag>())
        };
        
        foreach (var (evt, tags) in events)
        {
            var result = TestMultiProjector.Project(projector, evt, tags);
            if (result.IsSuccess)
            {
                projector = result.GetValue();
            }
        }
        
        return projector;
    }
    
    private Event CreateEvent(IEventPayload payload)
    {
        var timestamp = DateTime.UtcNow;
        var eventId = Guid.NewGuid();
        var sortableId = SortableUniqueId.Generate(timestamp, eventId);
        
        var metadata = new EventMetadata(
            CausationId: eventId.ToString(),
            CorrelationId: Guid.NewGuid().ToString(),
            ExecutedUser: "TestUser");
        
        return new Event(
            Payload: payload,
            SortableUniqueIdValue: sortableId,
            EventType: payload.GetType().Name,
            Id: eventId,
            EventMetadata: metadata,
            Tags: new List<string>());
    }
    
    [Fact]
    public async Task ExecuteQueryAsync_GetItemById_ReturnsCorrectItem()
    {
        // Arrange
        var projector = CreateSampleProjector();
        var targetItem = projector.Items.First();
        var query = new GetItemByIdQueryV2(targetItem.Id);
        
        // Act
        var result = await _queryExecutor.ExecuteQueryAsync<TestMultiProjector, GetItemByIdQueryV2, TestItem>(
            query,
            () => Task.FromResult(ResultBox.FromValue(projector)));
        
        // Assert
        Assert.True(result.IsSuccess);
        var item = result.GetValue();
        Assert.NotNull(item);
        Assert.Equal(targetItem.Id, item.Id);
        Assert.Equal(targetItem.Name, item.Name);
    }
    
    [Fact]
    public async Task ExecuteQueryAsync_GetItemCountByCategory_ReturnsCorrectCount()
    {
        // Arrange
        var projector = CreateSampleProjector();
        var query = new GetItemCountByCategoryQueryV2("Electronics");
        
        // Act
        var result = await _queryExecutor.ExecuteQueryAsync<TestMultiProjector, GetItemCountByCategoryQueryV2, int>(
            query,
            () => Task.FromResult(ResultBox.FromValue(projector)));
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.GetValue());
    }
    
    [Fact]
    public async Task ExecuteListQueryAsync_GetAllItems_ReturnsAllItemsSorted()
    {
        // Arrange
        var projector = CreateSampleProjector();
        var query = new GetAllItemsQueryV2();
        
        // Act
        var result = await _queryExecutor.ExecuteListQueryAsync<TestMultiProjector, GetAllItemsQueryV2, TestItem>(
            query,
            () => Task.FromResult(ResultBox.FromValue(projector)));
        
        // Assert
        Assert.True(result.IsSuccess);
        var queryResult = result.GetValue();
        Assert.Equal(5, queryResult.TotalCount);
        Assert.Equal(5, queryResult.Items.Count());
        
        // Verify sorting by creation date
        var items = queryResult.Items.ToList();
        for (int i = 1; i < items.Count; i++)
        {
            Assert.True(items[i - 1].CreatedAt <= items[i].CreatedAt);
        }
    }
    
    [Fact]
    public async Task ExecuteListQueryAsync_WithPagination_ReturnsCorrectPage()
    {
        // Arrange
        var projector = CreateSampleProjector();
        var query = new GetAllItemsQueryV2 { PageSize = 2, PageNumber = 1 };
        
        // Act
        var result = await _queryExecutor.ExecuteListQueryAsync<TestMultiProjector, GetAllItemsQueryV2, TestItem>(
            query,
            () => Task.FromResult(ResultBox.FromValue(projector)));
        
        // Assert
        Assert.True(result.IsSuccess);
        var queryResult = result.GetValue();
        Assert.Equal(5, queryResult.TotalCount);
        Assert.Equal(3, queryResult.TotalPages); // 5 items / 2 per page = 3 pages
        Assert.Equal(1, queryResult.CurrentPage);
        Assert.Equal(2, queryResult.PageSize);
        Assert.Equal(2, queryResult.Items.Count());
    }
    
    [Fact]
    public async Task ExecuteListQueryAsync_SecondPage_ReturnsCorrectItems()
    {
        // Arrange
        var projector = CreateSampleProjector();
        var query = new GetAllItemsQueryV2 { PageSize = 2, PageNumber = 2 };
        
        // Act
        var result = await _queryExecutor.ExecuteListQueryAsync<TestMultiProjector, GetAllItemsQueryV2, TestItem>(
            query,
            () => Task.FromResult(ResultBox.FromValue(projector)));
        
        // Assert
        Assert.True(result.IsSuccess);
        var queryResult = result.GetValue();
        Assert.Equal(2, queryResult.CurrentPage);
        Assert.Equal(2, queryResult.Items.Count());
    }
    
    [Fact]
    public async Task ExecuteListQueryAsync_LastPage_ReturnsRemainingItems()
    {
        // Arrange
        var projector = CreateSampleProjector();
        var query = new GetAllItemsQueryV2 { PageSize = 2, PageNumber = 3 };
        
        // Act
        var result = await _queryExecutor.ExecuteListQueryAsync<TestMultiProjector, GetAllItemsQueryV2, TestItem>(
            query,
            () => Task.FromResult(ResultBox.FromValue(projector)));
        
        // Assert
        Assert.True(result.IsSuccess);
        var queryResult = result.GetValue();
        Assert.Equal(3, queryResult.CurrentPage);
        Assert.Single(queryResult.Items); // Last page has only 1 item
    }
    
    [Fact]
    public async Task ExecuteListQueryAsync_FilterByCategory_ReturnsOnlyMatchingItems()
    {
        // Arrange
        var projector = CreateSampleProjector();
        var query = new GetItemsByCategoryQueryV2("Books");
        
        // Act
        var result = await _queryExecutor.ExecuteListQueryAsync<TestMultiProjector, GetItemsByCategoryQueryV2, TestItem>(
            query,
            () => Task.FromResult(ResultBox.FromValue(projector)));
        
        // Assert
        Assert.True(result.IsSuccess);
        var queryResult = result.GetValue();
        Assert.Equal(2, queryResult.TotalCount);
        Assert.All(queryResult.Items, item => Assert.Equal("Books", item.Category));
    }
    
    [Fact]
    public async Task ExecuteWithHandlerAsync_CustomHandler_ReturnsCorrectResult()
    {
        // Arrange
        var projector = CreateSampleProjector();
        
        // Act
        var result = await _queryExecutor.ExecuteWithHandlerAsync<TestMultiProjector, decimal>(
            () => Task.FromResult(ResultBox.FromValue(projector)),
            (p, context) => ResultBox.FromValue(p.Items.Sum(i => i.Price)));
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(440m, result.GetValue()); // 100 + 25 + 250 + 15 + 50
    }
    
    [Fact]
    public async Task ExecuteListWithHandlersAsync_CustomFiltersAndSort_ReturnsCorrectResult()
    {
        // Arrange
        var projector = CreateSampleProjector();
        var pagingParam = new TestPagingParameter { PageSize = 2, PageNumber = 1 };
        
        // Act
        var result = await _queryExecutor.ExecuteListWithHandlersAsync<TestMultiProjector, TestItem>(
            () => Task.FromResult(ResultBox.FromValue(projector)),
            (p, context) => ResultBox.FromValue(p.Items.Where(i => i.Price >= 50m).AsEnumerable()),
            (items, context) => ResultBox.FromValue(items.OrderBy(i => i.Price).AsEnumerable()),
            pagingParam);
        
        // Assert
        Assert.True(result.IsSuccess);
        var queryResult = result.GetValue();
        Assert.Equal(3, queryResult.TotalCount); // Items with price >= 50
        Assert.Equal(2, queryResult.TotalPages);
        Assert.Equal(2, queryResult.Items.Count());
        
        var itemsList = queryResult.Items.ToList();
        Assert.Equal(50m, itemsList[0].Price); // First item should be cheapest (50)
        Assert.Equal(100m, itemsList[1].Price); // Second item should be 100
    }
    
    [Fact]
    public async Task ExecuteQueryAsync_ProjectorProviderFails_ReturnsError()
    {
        // Arrange
        var query = new GetItemCountByCategoryQueryV2("Electronics");
        
        // Act
        var result = await _queryExecutor.ExecuteQueryAsync<TestMultiProjector, GetItemCountByCategoryQueryV2, int>(
            query,
            () => Task.FromResult(ResultBox.Error<TestMultiProjector>(new Exception("Provider failed"))));
        
        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Provider failed", result.GetException().Message);
    }
    
    private class TestPagingParameter : IQueryPagingParameter
    {
        public int? PageSize { get; init; }
        public int? PageNumber { get; init; }
    }
}