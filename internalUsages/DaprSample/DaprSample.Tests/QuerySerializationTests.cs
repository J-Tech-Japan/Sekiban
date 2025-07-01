using DaprSample.Domain;
using DaprSample.Domain.Generated;
using DaprSample.Domain.User.Queries;
using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Dapr.Serialization;
using System.Text.Json;
using Xunit;

namespace DaprSample.Tests;

public class QuerySerializationTests
{
    private readonly SekibanDomainTypes _domainTypes;
    private readonly JsonSerializerOptions _jsonOptions;

    public QuerySerializationTests()
    {
        _jsonOptions = DaprSampleEventsJsonContext.Default.Options;
        _domainTypes = DaprSampleDomainDomainTypes.Generate(_jsonOptions);
        // Use the domain types' JsonSerializerOptions which includes all types
        _jsonOptions = _domainTypes.JsonSerializerOptions;
    }

    [Fact]
    public async Task UserQuery_CanBeSerializedAndDeserialized()
    {
        // Arrange
        var originalQuery = new UserQuery(Guid.NewGuid())
        {
            WaitForSortableUniqueId = "test-sortable-id"
        };

        // Act - Serialize
        var serialized = await SerializableQuery.CreateFromAsync(originalQuery, _jsonOptions);
        
        // Assert serialization worked
        Assert.NotNull(serialized);
        Assert.NotEmpty(serialized.QueryTypeName);
        Assert.NotEmpty(serialized.CompressedQueryJson);
        Assert.Contains("UserQuery", serialized.QueryTypeName);

        // Act - Deserialize
        var deserializedResult = await serialized.ToQueryAsync(_domainTypes);
        
        // Assert deserialization worked
        Assert.True(deserializedResult.IsSuccess);
        var deserializedQuery = deserializedResult.GetValue() as UserQuery;
        Assert.NotNull(deserializedQuery);
        Assert.Equal(originalQuery.UserId, deserializedQuery.UserId);
        Assert.Equal(originalQuery.WaitForSortableUniqueId, deserializedQuery.WaitForSortableUniqueId);
    }

    [Fact]
    public async Task UserListQuery_CanBeSerializedAndDeserialized()
    {
        // Arrange
        var originalQuery = new UserListQuery("John", "test@example.com")
        {
            WaitForSortableUniqueId = "test-sortable-id"
        };

        // Act - Serialize
        var serialized = await SerializableListQuery.CreateFromAsync(originalQuery, _jsonOptions);
        
        // Assert serialization worked
        Assert.NotNull(serialized);
        Assert.NotEmpty(serialized.QueryTypeName);
        Assert.NotEmpty(serialized.CompressedQueryJson);
        Assert.Contains("UserListQuery", serialized.QueryTypeName);

        // Act - Deserialize
        var deserializedResult = await serialized.ToListQueryAsync(_domainTypes);
        
        // Assert deserialization worked
        Assert.True(deserializedResult.IsSuccess);
        var deserializedQuery = deserializedResult.GetValue() as UserListQuery;
        Assert.NotNull(deserializedQuery);
        Assert.Equal(originalQuery.NameContains, deserializedQuery.NameContains);
        Assert.Equal(originalQuery.EmailContains, deserializedQuery.EmailContains);
        Assert.Equal(originalQuery.WaitForSortableUniqueId, deserializedQuery.WaitForSortableUniqueId);
    }

    [Fact]
    public async Task UserStatisticsQuery_CanBeSerializedAndDeserialized()
    {
        // Arrange
        var originalQuery = new UserStatisticsQuery
        {
            WaitForSortableUniqueId = "test-sortable-id"
        };

        // Act - Serialize
        var serialized = await SerializableQuery.CreateFromAsync(originalQuery, _jsonOptions);
        
        // Assert serialization worked
        Assert.NotNull(serialized);
        Assert.NotEmpty(serialized.QueryTypeName);
        Assert.NotEmpty(serialized.CompressedQueryJson);
        Assert.Contains("UserStatisticsQuery", serialized.QueryTypeName);

        // Act - Deserialize
        var deserializedResult = await serialized.ToQueryAsync(_domainTypes);
        
        // Assert deserialization worked
        Assert.True(deserializedResult.IsSuccess);
        var deserializedQuery = deserializedResult.GetValue() as UserStatisticsQuery;
        Assert.NotNull(deserializedQuery);
        Assert.Equal(originalQuery.WaitForSortableUniqueId, deserializedQuery.WaitForSortableUniqueId);
    }

    [Fact]
    public async Task QueryResult_CanBeSerializedAndDeserialized()
    {
        // Arrange
        var userDetail = new UserQuery.UserDetail(
            Guid.NewGuid(),
            "John Doe",
            "john@example.com",
            1,
            "last-event-id"
        );
        var originalQuery = new UserQuery(userDetail.UserId);
        var queryResultGeneral = new Sekiban.Pure.Query.QueryResultGeneral(
            userDetail,
            typeof(UserQuery.UserDetail).FullName!,
            originalQuery
        );

        // Act - Serialize
        var serialized = await SerializableQueryResult.CreateFromAsync(queryResultGeneral, _jsonOptions);
        
        // Assert serialization worked
        Assert.NotNull(serialized);
        Assert.NotEmpty(serialized.ResultTypeName);
        Assert.NotEmpty(serialized.QueryTypeName);
        Assert.NotEmpty(serialized.CompressedResultJson);
        Assert.NotEmpty(serialized.CompressedQueryJson);

        // Act - Deserialize
        var deserializedResult = await serialized.ToQueryResultAsync(_domainTypes);
        
        // Assert deserialization worked
        if (!deserializedResult.IsSuccess)
        {
            var exception = deserializedResult.GetException();
            Assert.True(deserializedResult.IsSuccess, $"Deserialization failed: {exception.Message}");
        }
        var queryResult = deserializedResult.GetValue();
        Assert.NotNull(queryResult);
        
        var resultValue = queryResult.Value as UserQuery.UserDetail;
        Assert.NotNull(resultValue);
        Assert.Equal(userDetail.UserId, resultValue.UserId);
        Assert.Equal(userDetail.Name, resultValue.Name);
        Assert.Equal(userDetail.Email, resultValue.Email);
        Assert.Equal(userDetail.Version, resultValue.Version);
        Assert.Equal(userDetail.LastEventId, resultValue.LastEventId);
    }
}