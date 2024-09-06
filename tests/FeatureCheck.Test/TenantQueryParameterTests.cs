using FeatureCheck.Domain.Aggregates.Branches;
using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
namespace FeatureCheck.Test;

public class TenantQueryParameterTests
{

    [Fact]
    public void Should_Return_Tenant_TestNextAggregateQuery()
    {
        var query = new TestNextAggregateQuery("tenant1");
        // Act
        var result = ((IQueryPartitionKeyCommon)query).GetRootPartitionKey();

        // Assert
        Assert.Equal("tenant1", result);
    }
    [Fact]
    public void Should_Return_Tenant_TestNextAggregateListQuery()
    {
        var query = new TestNextAggregateListQuery("tenant1");
        // Act
        var result = ((IQueryPartitionKeyCommon)query).GetRootPartitionKey();

        // Assert
        Assert.Equal("tenant1", result);
    }
    [Fact]
    public void Should_Return_Tenant_TestNextAggregateQueryAsync()
    {
        var query = new TestNextAggregateQueryAsync("tenant1");
        // Act
        var result = ((IQueryPartitionKeyCommon)query).GetRootPartitionKey();
        // Assert
        Assert.Equal("tenant1", result);
    }
    [Fact]
    public void Should_Return_Tenant_TestNextAggregateListQueryAsync()
    {
        var query = new TestNextAggregateQueryAsync("tenant1");
        // Act
        var result = ((IQueryPartitionKeyCommon)query).GetRootPartitionKey();
        // Assert
        Assert.Equal("tenant1", result);
    }
    [Fact]
    public void Should_Return_Tenant_TestAggregateQuery()
    {
        var query = new TestAggregateQuery.Parameter("tenant1");
        // Act
        var result = ((IQueryPartitionKeyCommon)query).GetRootPartitionKey();
        // Assert
        Assert.Equal("tenant1", result);
    }
    [Fact]
    public void Should_Return_Tenant_TestAggregateListQuery()
    {
        var query = new TestAggregateListQuery.Parameter("tenant1");
        // Act
        var result = ((IQueryPartitionKeyCommon)query).GetRootPartitionKey();
        // Assert
        Assert.Equal("tenant1", result);
    }
    public class
        TestAggregateQuery : ITenantAggregateQuery<Branch, TestAggregateQuery.Parameter, TestAggregateQuery.Record>
    {
        public Record HandleFilter(Parameter queryParam, IEnumerable<AggregateState<Branch>> list) =>
            throw new NotImplementedException();
        public record Record(bool Flag) : IQueryResponse;
        public record Parameter(string Tenant) : ITenantQueryParameter<Record>
        {
            public string GetTenantId() => Tenant;
        }
    }
    public class TestAggregateListQuery : ITenantAggregateListQuery<Branch, TestAggregateListQuery.Parameter,
        TestAggregateListQuery.Record>
    {
        public IEnumerable<Record> HandleFilter(Parameter queryParam, IEnumerable<AggregateState<Branch>> list) =>
            throw new NotImplementedException();
        public IEnumerable<Record> HandleSort(Parameter queryParam, IEnumerable<Record> filteredList) =>
            throw new NotImplementedException();
        public record Record(bool Flag) : IQueryResponse;
        public record Parameter(string Tenant) : ITenantListQueryParameter<Record>
        {
            public string GetTenantId() => Tenant;
        }
    }
    public record TestNextAggregateQuery(string Tenant)
        : ITenantNextAggregateQuery<Branch, TestNextAggregateQuery, bool>
    {
        public string GetTenantId() => Tenant;
        public static ResultBox<bool> HandleFilter(
            IEnumerable<AggregateState<Branch>> list,
            TestNextAggregateQuery query,
            IQueryContext context) => throw new NotImplementedException();
    }
    public record TestNextAggregateQueryAsync(string Tenant)
        : ITenantNextAggregateQueryAsync<Branch, TestNextAggregateQueryAsync, bool>
    {

        public string GetTenantId() => Tenant;
        public static Task<ResultBox<bool>> HandleFilterAsync(
            IEnumerable<AggregateState<Branch>> list,
            TestNextAggregateQueryAsync query,
            IQueryContext context) => throw new NotImplementedException();
    }
    public record TestNextAggregateListQuery(string Tenant)
        : ITenantNextAggregateListQuery<Branch, TestNextAggregateListQuery, bool>
    {
        public string GetTenantId() => Tenant;
        public static ResultBox<IEnumerable<bool>> HandleFilter(
            IEnumerable<AggregateState<Branch>> list,
            TestNextAggregateListQuery query,
            IQueryContext context) => throw new NotImplementedException();
        public static ResultBox<IEnumerable<bool>> HandleSort(
            IEnumerable<bool> filteredList,
            TestNextAggregateListQuery query,
            IQueryContext context) => throw new NotImplementedException();
    }
    public record TestNextAggregateListQueryAsync(string Tenant)
        : ITenantNextAggregateListQueryAsync<Branch, TestNextAggregateListQueryAsync, bool>
    {
        public string GetTenantId() => Tenant;
        public static Task<ResultBox<IEnumerable<bool>>> HandleFilterAsync(IEnumerable<AggregateState<Branch>> list, TestNextAggregateListQueryAsync query, IQueryContext context) => throw new NotImplementedException();
        public static Task<ResultBox<IEnumerable<bool>>> HandleSortAsync(IEnumerable<bool> filteredList, TestNextAggregateListQueryAsync query, IQueryContext context) => throw new NotImplementedException();
    }
}
