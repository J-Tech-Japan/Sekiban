using ResultBoxes;
using Sekiban.Core.Query.QueryModel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
namespace FeatureCheck.Test;

public class TenantQueryParameterGeneralTests
{

    [Fact]
    public void Should_Return_Tenant_TestNextAggregateQuery()
    {
        var query = new TestNextGeneralQuery("tenant1");
        // Act
        var result = ((IQueryPartitionKeyCommon)query).GetRootPartitionKey();

        // Assert
        Assert.Equal("tenant1", result);
    }
    [Fact]
    public void Should_Return_Tenant_TestNextAggregateListQuery()
    {
        var query = new TestNextGeneralListQuery("tenant1");
        // Act
        var result = ((IQueryPartitionKeyCommon)query).GetRootPartitionKey();

        // Assert
        Assert.Equal("tenant1", result);
    }
    [Fact]
    public void Should_Return_Tenant_TestNextAggregateQueryAsync()
    {
        var query = new TestNextGeneralQueryAsync("tenant1");
        // Act
        var result = ((IQueryPartitionKeyCommon)query).GetRootPartitionKey();
        // Assert
        Assert.Equal("tenant1", result);
    }
    [Fact]
    public void Should_Return_Tenant_TestNextAggregateListQueryAsync()
    {
        var query = new TestNextGeneralQueryAsync("tenant1");
        // Act
        var result = ((IQueryPartitionKeyCommon)query).GetRootPartitionKey();
        // Assert
        Assert.Equal("tenant1", result);
    }
    public record TestNextGeneralQuery(string Tenant) : ITenantNextGeneralQuery<TestNextGeneralQuery, bool>
    {
        public string GetTenantId() => Tenant;
        public static ResultBox<bool> HandleFilter(TestNextGeneralQuery query, IQueryContext context) =>
            throw new NotImplementedException();
    }
    public record TestNextGeneralQueryAsync(string Tenant)
        : ITenantNextGeneralQueryAsync<TestNextGeneralQueryAsync, bool>
    {
        public string GetTenantId() => Tenant;
        public static Task<ResultBox<bool>> HandleFilterAsync(TestNextGeneralQueryAsync query, IQueryContext context) =>
            throw new NotImplementedException();
    }
    public record TestNextGeneralListQuery(string Tenant) : ITenantNextGeneralListQuery<TestNextGeneralListQuery, bool>
    {
        public string GetTenantId() => Tenant;
        public static ResultBox<IEnumerable<bool>>
            HandleFilter(TestNextGeneralListQuery query, IQueryContext context) => throw new NotImplementedException();
        public static ResultBox<IEnumerable<bool>> HandleSort(
            IEnumerable<bool> filteredList,
            TestNextGeneralListQuery query,
            IQueryContext context) => throw new NotImplementedException();
    }
    public record TestNextGeneralListQueryAsync(string Tenant)
        : ITenantNextGeneralListQueryAsync<TestNextGeneralListQueryAsync, bool>
    {
        public string GetTenantId() => Tenant;
        public static Task<ResultBox<IEnumerable<bool>>> HandleFilterAsync(
            TestNextGeneralListQueryAsync query,
            IQueryContext context) => throw new NotImplementedException();
        public static Task<ResultBox<IEnumerable<bool>>> HandleSortAsync(
            IEnumerable<bool> filteredList,
            TestNextGeneralListQueryAsync query,
            IQueryContext context) => throw new NotImplementedException();
    }
}
