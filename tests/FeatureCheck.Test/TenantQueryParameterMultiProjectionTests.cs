using FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;
using ResultBoxes;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
namespace FeatureCheck.Test;

public class TenantQueryParameterMultiProjectionTests
{

    [Fact]
    public void Should_Return_Tenant_TestNextAggregateQuery()
    {
        var query = new TestNextMultiProjectionQuery("tenant1");
        // Act
        var result = ((IQueryPartitionKeyCommon)query).GetRootPartitionKey();

        // Assert
        Assert.Equal("tenant1", result);
    }
    [Fact]
    public void Should_Return_Tenant_TestNextAggregateListQuery()
    {
        var query = new TestNextMultiProjectionListQuery("tenant1");
        // Act
        var result = ((IQueryPartitionKeyCommon)query).GetRootPartitionKey();

        // Assert
        Assert.Equal("tenant1", result);
    }
    [Fact]
    public void Should_Return_Tenant_TestNextAggregateQueryAsync()
    {
        var query = new TestNextMultiProjectionQueryAsync("tenant1");
        // Act
        var result = ((IQueryPartitionKeyCommon)query).GetRootPartitionKey();
        // Assert
        Assert.Equal("tenant1", result);
    }
    [Fact]
    public void Should_Return_Tenant_TestNextAggregateListQueryAsync()
    {
        var query = new TestNextMultiProjectionQueryAsync("tenant1");
        // Act
        var result = ((IQueryPartitionKeyCommon)query).GetRootPartitionKey();
        // Assert
        Assert.Equal("tenant1", result);
    }
    public record TestNextMultiProjectionQuery(string Tenant) : ITenantNextMultiProjectionQuery<ClientLoyaltyPointMultiProjection,bool>
    {
        public string GetTenantId() => Tenant;
        public ResultBox<bool> HandleFilter(MultiProjectionState<ClientLoyaltyPointMultiProjection> projection, IQueryContext context) => throw new NotImplementedException();
    }
    public record TestNextMultiProjectionQueryAsync(string Tenant) : ITenantNextMultiProjectionQueryAsync<ClientLoyaltyPointMultiProjection,bool>
    {
        public string GetTenantId() => Tenant;
        public Task<ResultBox<bool>> HandleFilterAsync(MultiProjectionState<ClientLoyaltyPointMultiProjection> projection, IQueryContext context) => throw new NotImplementedException();
    }
    public record TestNextMultiProjectionListQuery(string Tenant) : ITenantNextMultiProjectionListQuery<ClientLoyaltyPointMultiProjection,bool>
    {
        public string GetTenantId() => Tenant;
        public ResultBox<IEnumerable<bool>> HandleFilter(MultiProjectionState<ClientLoyaltyPointMultiProjection> projection, IQueryContext context) => throw new NotImplementedException();
        public ResultBox<IEnumerable<bool>> HandleSort(IEnumerable<bool> filteredList, IQueryContext context) =>
            throw new NotImplementedException();
    }
    public record TestNextMultiProjectionListQueryAsync(string Tenant) : ITenantNextMultiProjectionListQueryAsync<ClientLoyaltyPointMultiProjection,bool>
    {
        public string GetTenantId() => Tenant;
        public Task<ResultBox<IEnumerable<bool>>> HandleFilterAsync(MultiProjectionState<ClientLoyaltyPointMultiProjection> projection, IQueryContext context) => throw new NotImplementedException();
        public Task<ResultBox<IEnumerable<bool>>> HandleSortAsync(
            IEnumerable<bool> filteredList,
            IQueryContext context) =>
            throw new NotImplementedException();
    }
}