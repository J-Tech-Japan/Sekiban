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
        public ResultBox<bool> HandleFilter(IQueryContext context) => throw new NotImplementedException();
    }
    public record TestNextGeneralQueryAsync(string Tenant)
        : ITenantNextGeneralQueryAsync<TestNextGeneralQueryAsync, bool>
    {
        public string GetTenantId() => Tenant;
        public Task<ResultBox<bool>> HandleFilterAsync(IQueryContext context) => throw new NotImplementedException();
    }
    public record TestNextGeneralListQuery(string Tenant) : ITenantNextGeneralListQuery<TestNextGeneralListQuery, bool>
    {
        public string GetTenantId() => Tenant;
        public ResultBox<IEnumerable<bool>> HandleFilter(IQueryContext context) => throw new NotImplementedException();
        public ResultBox<IEnumerable<bool>> HandleSort(IEnumerable<bool> filteredList, IQueryContext context) =>
            throw new NotImplementedException();
    }
    public record TestNextGeneralListQueryAsync(string Tenant)
        : ITenantNextGeneralListQueryAsync<TestNextGeneralListQueryAsync, bool>
    {
        public string GetTenantId() => Tenant;
        public Task<ResultBox<IEnumerable<bool>>> HandleFilterAsync(IQueryContext context) =>
            throw new NotImplementedException();
        public Task<ResultBox<IEnumerable<bool>>> HandleSortAsync(
            IEnumerable<bool> filteredList,
            IQueryContext context) =>
            throw new NotImplementedException();
    }
}
