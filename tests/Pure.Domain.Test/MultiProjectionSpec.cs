using Microsoft.Extensions.DependencyInjection;
using Pure.Domain.Generated;
using ResultBoxes;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Repositories;
namespace Pure.Domain.Test;

public class MultiProjectionSpec
{
    [Fact]
    public async Task TestSimple()
    {
        InMemorySekibanExecutor executor = new(
            PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
            new FunctionCommandMetadataProvider(() => "test"),
            new Repository(),
            new ServiceCollection().BuildServiceProvider());
        var result = await executor
            .CommandAsync(new RegisterUser("Tomohisa", "tomo@example.com"))
            .Conveyor(response => executor.CommandAsync(new ConfirmUser(response.PartitionKeys.AggregateId)))
            .Conveyor(_ => executor.CommandAsync(new RegisterUser("John", "john@example.com")))
            .Conveyor(response => executor.CommandAsync(new ConfirmUser(response.PartitionKeys.AggregateId)))
            .Conveyor(response => executor.CommandAsync(new RevokeUser(response.PartitionKeys.AggregateId)))
            .Conveyor(_ => executor.CommandAsync(new RegisterBranch("japan")));
        Assert.True(result.IsSuccess);
        var projectionResult
            = await executor.Repository.LoadMultiProjection<MultiProjectorPayload>(MultiProjectionEventSelector.All);
        Assert.True(projectionResult.IsSuccess);
        var projection = projectionResult.GetValue();
        Assert.Equal(2, projection.Payload.Users.Count);
        Assert.Equal(1, projection.Payload.Users.Values.Count(m => m.IsConfirmed));

        var projectionFromAggregateList
            = await executor.Repository.LoadMultiProjection<AggregateListProjector<UserProjector>>(
                MultiProjectionEventSelector.FromProjectorGroup<UserProjector>());
        // var projectionFromAggregateList
        //     = Repository.LoadMultiProjection<AggregateListProjector<UserProjector>>(MultiProjectionEventSelector.All);
        Assert.True(projectionFromAggregateList.IsSuccess);
        var projectionFromAggregateListValue = projectionFromAggregateList.GetValue();
        Assert.Equal(2, projectionFromAggregateListValue.Payload.Aggregates.Count);
        Assert.Equal(
            1,
            projectionFromAggregateListValue.Payload.Aggregates.Count(m => m.Value.GetPayload() is ConfirmedUser));
        var queryResult = await executor.QueryAsync(new UserQueryFromMultiProjection());
        // var queryResult
        //     = await queryExecutor
        //         .ExecuteListWithMultiProjectionFunction<MultiProjectorPayload, UserQueryFromMultiProjection, string>(
        //             new UserQueryFromMultiProjection(),
        //             UserQueryFromMultiProjection.HandleFilter,
        //             UserQueryFromMultiProjection.HandleSort);
        Assert.True(queryResult.IsSuccess);
        var queryResultValue = queryResult.GetValue().Items.ToList();
        Assert.Equal(2, queryResultValue.Count);
    }

    [Fact]
    public async Task TestUserQueryFromAggregateProjection()
    {
        InMemorySekibanExecutor executor = new(
            PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
            new FunctionCommandMetadataProvider(() => "test"),
            new Repository(),
            new ServiceCollection().BuildServiceProvider());
        var result = await executor
            // Register and confirm first user "Alice"
            .CommandAsync(new RegisterUser("Alice", "alice@example.com"))
            .Conveyor(response => executor.CommandAsync(new ConfirmUser(response.PartitionKeys.AggregateId)))
            // Register and confirm second user "Bob"
            .Conveyor(_ => executor.CommandAsync(new RegisterUser("Bob", "bob@example.com")))
            .Conveyor(response => executor.CommandAsync(new ConfirmUser(response.PartitionKeys.AggregateId)))
            // Register third user "Charlie" but don't confirm
            .Conveyor(_ => executor.CommandAsync(new RegisterUser("Charlie", "charlie@example.com")));

        Assert.True(result.IsSuccess);

        // Test 1: Query with empty filter (should return all confirmed users)
        var allUsersQuery = await executor.QueryAsync(new UserQueryFromAggregateProjection(""));
        Assert.True(allUsersQuery.IsSuccess);
        var allUsers = allUsersQuery.GetValue().Items.ToList();
        Assert.Equal(2, allUsers.Count); // Only confirmed users (Alice and Bob)
        Assert.Equal("Alice", allUsers[0]); // Verify sorting
        Assert.Equal("Bob", allUsers[1]);

        // Test 2: Query with name filter
        var filteredQuery = await executor.QueryAsync(new UserQueryFromAggregateProjection("ob")); // Should match "Bob"
        Assert.True(filteredQuery.IsSuccess);
        var filteredUsers = filteredQuery.GetValue().Items.ToList();
        Assert.Single(filteredUsers);
        Assert.Equal("Bob", filteredUsers[0]);

        // Test 3: Query with non-matching filter
        var noMatchQuery = await executor.QueryAsync(new UserQueryFromAggregateProjection("xyz"));
        Assert.True(noMatchQuery.IsSuccess);
        var noMatchUsers = noMatchQuery.GetValue().Items.ToList();
        Assert.Empty(noMatchUsers);
    }

    [Fact]
    public async Task TestUserExistsQuery()
    {
        InMemorySekibanExecutor executor = new(
            PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
            new FunctionCommandMetadataProvider(() => "test"),
            new Repository(),
            new ServiceCollection().BuildServiceProvider());
        var result = await executor
            // Register first user
            .CommandAsync(new RegisterUser("Alice", "alice@example.com"))
            // Register second user
            .Conveyor(_ => executor.CommandAsync(new RegisterUser("Bob", "bob@example.com")));

        Assert.True(result.IsSuccess);

        // Test 1: Query with existing email
        var existingEmailQuery = await executor.QueryAsync(new UserExistsQueryFromMultiProjection("alice@example.com"));
        Assert.True(existingEmailQuery.IsSuccess);
        Assert.True(existingEmailQuery.GetValue());

        // Test 2: Query with another existing email
        var anotherExistingEmailQuery = await executor.QueryAsync(
            new UserExistsQueryFromMultiProjection("bob@example.com"));
        Assert.True(anotherExistingEmailQuery.IsSuccess);
        Assert.True(anotherExistingEmailQuery.GetValue());

        // Test 3: Query with non-existing email
        var nonExistingEmailQuery = await executor.QueryAsync(
            new UserExistsQueryFromMultiProjection("nonexistent@example.com"));
        Assert.True(nonExistingEmailQuery.IsSuccess);
        Assert.False(nonExistingEmailQuery.GetValue());
    }

    [Fact]
    public async Task TestUserExistsQueryFromAggregateListProjection()
    {
        InMemorySekibanExecutor executor = new(
            PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
            new FunctionCommandMetadataProvider(() => "test"),
            new Repository(),
            new ServiceCollection().BuildServiceProvider());
        var result = await executor
            // Register first user
            .CommandAsync(new RegisterUser("Alice", "alice@example.com"))
            // Register second user
            .Conveyor(_ => executor.CommandAsync(new RegisterUser("Bob", "bob@example.com")));

        Assert.True(result.IsSuccess);

        // Test 1: Query with existing email
        var existingEmailQuery = await executor.QueryAsync(
            new UserExistsQueryFromAggregateListProjection("alice@example.com"));
        Assert.True(existingEmailQuery.IsSuccess);
        Assert.True(existingEmailQuery.GetValue());

        // Test 2: Query with another existing email
        var anotherExistingEmailQuery = await executor.QueryAsync(
            new UserExistsQueryFromAggregateListProjection("bob@example.com"));
        Assert.True(anotherExistingEmailQuery.IsSuccess);
        Assert.True(anotherExistingEmailQuery.GetValue());

        // Test 3: Query with non-existing email
        var nonExistingEmailQuery = await executor.QueryAsync(
            new UserExistsQueryFromAggregateListProjection("nonexistent@example.com"));
        Assert.True(nonExistingEmailQuery.IsSuccess);
        Assert.False(nonExistingEmailQuery.GetValue());
    }
}
