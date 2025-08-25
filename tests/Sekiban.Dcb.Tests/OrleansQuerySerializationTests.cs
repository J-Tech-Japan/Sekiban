using Sekiban.Dcb.Queries;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for Orleans query serialization types
/// </summary>
public class OrleansQuerySerializationTests
{
    [Fact]
    public void QueryResultGeneral_CanSerializeAndDeserialize()
    {
        // Arrange
        var testValue = new TestQueryResult { Id = 123, Name = "Test" };
        var query = new TestQuery();
        var result = new QueryResultGeneral(testValue, typeof(TestQueryResult).FullName!, query);

        // Act - Convert to typed and back
        var typedResult = result.ToTypedResult<TestQueryResult>();

        // Assert
        Assert.True(typedResult.IsSuccess);
        var value = typedResult.GetValue();
        Assert.Equal(123, value.Id);
        Assert.Equal("Test", value.Name);
    }

    [Fact]
    public void ListQueryResultGeneral_CanSerializeAndDeserialize()
    {
        // Arrange
        var items = new List<TestQueryResult>
        {
            new() { Id = 1, Name = "Item1" },
            new() { Id = 2, Name = "Item2" },
            new() { Id = 3, Name = "Item3" }
        };

        var query = new TestListQuery();
        var result = new ListQueryResultGeneral(
            3, // TotalCount
            1, // TotalPages
            1, // CurrentPage
            10, // PageSize
            items,
            typeof(TestQueryResult).FullName!,
            query);

        // Act - Convert to typed result
        var typedResult = result.ToTypedResult<TestQueryResult>();

        // Assert
        Assert.True(typedResult.IsSuccess);
        var listResult = typedResult.GetValue();
        Assert.Equal(3, listResult.TotalCount);
        Assert.Equal(1, listResult.TotalPages);
        Assert.Equal(1, listResult.CurrentPage);
        Assert.Equal(10, listResult.PageSize);
        Assert.Equal(3, listResult.Items.Count());

        var itemsList = listResult.Items.ToList();
        Assert.Equal("Item1", itemsList[0].Name);
        Assert.Equal("Item2", itemsList[1].Name);
        Assert.Equal("Item3", itemsList[2].Name);
    }

    [Fact]
    public void QueryResultGeneral_HandlesNullValue()
    {
        // Arrange
        var query = new TestQuery();
        var result = new QueryResultGeneral(null!, string.Empty, query);

        // Act
        var typedResult = result.ToTypedResult<TestQueryResult>();

        // Assert
        Assert.False(typedResult.IsSuccess);
    }

    [Fact]
    public void ListQueryResultGeneral_EmptyReturnsEmptyResult()
    {
        // Arrange & Act
        var empty = ListQueryResultGeneral.Empty;

        // Assert
        Assert.Equal(0, empty.TotalCount);
        Assert.Equal(0, empty.TotalPages);
        Assert.Equal(0, empty.CurrentPage);
        Assert.Equal(0, empty.PageSize);
        Assert.Empty(empty.Items);
    }

    // Test types
    private record TestQueryResult
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
    }

    private class TestQuery : IQueryCommon<TestQueryResult>
    {
    }

    private class TestListQuery : IListQueryCommon<TestQueryResult>
    {
    }
}
