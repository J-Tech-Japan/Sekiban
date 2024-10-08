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
    public record TestNextMultiProjectionQuery(string Tenant)
        : ITenantNextMultiProjectionQuery<ClientLoyaltyPointMultiProjection, TestNextMultiProjectionQuery, bool>
    {
        public string GetTenantId() => Tenant;
        public static ResultBox<bool> HandleFilter(
            MultiProjectionState<ClientLoyaltyPointMultiProjection> projection,
            TestNextMultiProjectionQuery query,
            IQueryContext context) =>
            throw new NotImplementedException();
    }
    public record TestNextMultiProjectionQueryAsync(string Tenant)
        : ITenantNextMultiProjectionQueryAsync<ClientLoyaltyPointMultiProjection, TestNextMultiProjectionQueryAsync,
            bool>
    {
        public string GetTenantId() => Tenant;
        public static Task<ResultBox<bool>> HandleFilterAsync(
            MultiProjectionState<ClientLoyaltyPointMultiProjection> projection,
            TestNextMultiProjectionQueryAsync query,
            IQueryContext context) =>
            throw new NotImplementedException();
    }
    public record TestNextMultiProjectionListQuery(string Tenant)
        : ITenantNextMultiProjectionListQuery<ClientLoyaltyPointMultiProjection, TestNextMultiProjectionListQuery, bool>
    {
        public string GetTenantId() => Tenant;
        public static ResultBox<IEnumerable<bool>> HandleFilter(
            MultiProjectionState<ClientLoyaltyPointMultiProjection> projection,
            TestNextMultiProjectionListQuery query,
            IQueryContext context) =>
            throw new NotImplementedException();
        public static ResultBox<IEnumerable<bool>> HandleSort(
            IEnumerable<bool> filteredList,
            TestNextMultiProjectionListQuery query,
            IQueryContext context) => throw new NotImplementedException();
    }
    public record TestNextMultiProjectionListQueryAsync(string Tenant)
        : ITenantNextMultiProjectionListQueryAsync<ClientLoyaltyPointMultiProjection,
            TestNextMultiProjectionListQueryAsync, bool>
    {
        public string GetTenantId() => Tenant;
        public static Task<ResultBox<IEnumerable<bool>>> HandleFilterAsync(
            MultiProjectionState<ClientLoyaltyPointMultiProjection> projection,
            TestNextMultiProjectionListQueryAsync query,
            IQueryContext context) =>
            throw new NotImplementedException();
        public static Task<ResultBox<IEnumerable<bool>>> HandleSortAsync(
            IEnumerable<bool> filteredList,
            TestNextMultiProjectionListQueryAsync query,
            IQueryContext context) =>
            throw new NotImplementedException();
    }
}
