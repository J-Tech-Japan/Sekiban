﻿﻿using AspireEventSample.ApiService.Aggregates.Branches;
using AspireEventSample.ApiService.Generated;
using AspireEventSample.ApiService.Projections;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Pure;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Query;
using Sekiban.Pure.Repositories;
namespace AspireEventSample.UnitTest;

public class AspireEventSampleUnitTest
{
    private SekibanDomainTypes SekibanDomainTypes { get; }
        = AspireEventSampleApiServiceDomainTypes.Generate(AspireEventSampleApiServiceEventsJsonContext.Default.Options);
    private ICommandMetadataProvider CommandMetadataProvider { get; }
        = new FunctionCommandMetadataProvider(() => "test");
    [Fact]
    public void BranchExistsQueryTest()
    {
        var query = new BranchExistsQuery("Test");
        var queryResult = new QueryResult<bool>(true);
        var general = queryResult.ToGeneral(query);
        Assert.True((bool)general.GetValue());
    }

    [Fact]
    public async Task RegisterBranchTest()
    {
        var executor = new InMemorySekibanExecutor(
            SekibanDomainTypes,
            CommandMetadataProvider,
            new Repository(),
            new ServiceCollection().BuildServiceProvider());

        var result1 = await executor.CommandAsync(new RegisterBranch("DDD", "Japan"));
        Assert.True(result1.IsSuccess);
        var value = result1.GetValue();
        Assert.NotNull(value);
        Assert.Equal(1, value.Version);
        var aggregateId = value.PartitionKeys.AggregateId;
        Assert.NotEqual(Guid.Empty, aggregateId);
    }

    [Fact]
    public async Task SimpleBranchListQueryTest()
    {
        var executor = new InMemorySekibanExecutor(
            SekibanDomainTypes,
            CommandMetadataProvider,
            new Repository(),
            new ServiceCollection().BuildServiceProvider());

        // Register a branch to generate events
        var commandResult = await executor.CommandAsync(new RegisterBranch("TestList", "Japan"));
        Assert.True(commandResult.IsSuccess, "RegisterBranch command should succeed");

        var listQuery = new SimpleBranchListQuery("TestList");
        var queryResult = await executor.QueryAsync(listQuery);
        Assert.True(queryResult.IsSuccess, "List query execution should be successful");
    }

    [Fact]
    public async Task LoadAggregateTest()
    {
        var executor = new InMemorySekibanExecutor(
            SekibanDomainTypes,
            CommandMetadataProvider,
            new Repository(),
            new ServiceCollection().BuildServiceProvider());

        // Register a branch to generate events
        var commandResult = await executor.CommandAsync(new RegisterBranch("TestLoad", "Japan"));
        Assert.True(commandResult.IsSuccess, "RegisterBranch command should succeed");

        var aggregateId = commandResult.GetValue().PartitionKeys.AggregateId;
        var partitionKeys = PartitionKeys.Existing<BranchProjector>(aggregateId);

        // Load the aggregate
        var aggregateResult = await executor.LoadAggregateAsync<BranchProjector>(partitionKeys);
        Assert.True(aggregateResult.IsSuccess, "LoadAggregateAsync should succeed");

        var aggregate = aggregateResult.GetValue();
        Assert.NotNull(aggregate);
        Assert.Equal(aggregateId, aggregate.PartitionKeys.AggregateId);
        var branch = (Branch)aggregate.Payload;
        Assert.Equal("TestLoad", branch.Name);
        Assert.Equal("Japan", branch.Country);
    }
}
